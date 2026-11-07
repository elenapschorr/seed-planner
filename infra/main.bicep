// Seed Planner - Azure infrastructure
//
// Creates: Log Analytics, Container Apps environment, Container Registry,
// PostgreSQL Flexible Server (+ database) and, once an image exists, the container app.
//
// Two-step deployment (see infra/README.md):
//   1. deployApp=false  -> infrastructure only (the registry must exist before an image can be pushed)
//   2. deployApp=true   -> additionally creates the container app that runs the pushed image

targetScope = 'resourceGroup'

@description('Azure region. If PostgreSQL Burstable is not offered there, try another region such as westeurope.')
param location string = 'germanywestcentral'

@description('Lowercase letters and digits only. Used as prefix for resource names.')
@minLength(3)
@maxLength(12)
param appName string = 'seedplanner'

@description('Administrator login of the PostgreSQL server.')
param dbAdminUser string = 'seedadmin'

@description('Administrator password. Use letters and digits only (it is placed in a connection string).')
@secure()
@minLength(16)
param dbAdminPassword string

@description('Invitation code required to register a new user.')
@secure()
@minLength(10)
param inviteCode string

@description('false = infrastructure only (first run). true = also create the container app.')
param deployApp bool = false

@description('Tag of the image seed-planner in the registry. Only used when deployApp is true.')
param imageTag string = 'latest'

@description('Your public IP address, to reach the database with psql for the data import. Empty = no rule.')
param clientIp string = ''

@description('0 = scale to zero (cheapest, slow first request). 1 = always one replica.')
@allowed([ 0, 1 ])
param minReplicas int = 1

var suffix = uniqueString(resourceGroup().id)
var registryName = '${appName}${suffix}'
var serverName = '${appName}-pg-${suffix}'
var databaseName = 'seedplanner'

// Built-in role "AcrPull"
var acrPullRoleId = '7f951dda-4ed3-4680-a7ca-43fe172d538d'

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${appName}-logs'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${appName}-env'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: registryName
  location: location
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: false
  }
}

// The app pulls its image with a managed identity, so no registry password exists anywhere.
resource pullIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${appName}-pull'
  location: location
}

resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, pullIdentity.id, acrPullRoleId)
  scope: registry
  properties: {
    principalId: pullIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
  }
}

resource server 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: serverName
  location: location
  sku: {
    name: 'Standard_B1ms'
    tier: 'Burstable'
  }
  properties: {
    version: '17'
    administratorLogin: dbAdminUser
    administratorLoginPassword: dbAdminPassword
    storage: {
      storageSizeGB: 32
    }
    backup: {
      backupRetentionDays: 7
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: {
      mode: 'Disabled'
    }
    authConfig: {
      activeDirectoryAuth: 'Disabled'
      passwordAuth: 'Enabled'
    }
  }
}

resource database 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: server
  name: databaseName
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
}

// Firewall rules are created one after another; Azure rejects parallel changes on one server.
// 0.0.0.0 is the special "allow Azure services" rule (the container app connects from inside Azure).
resource allowAzure 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = {
  parent: server
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
  dependsOn: [ database ]
}

resource allowClient 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = if (!empty(clientIp)) {
  parent: server
  name: 'AllowClient'
  properties: {
    startIpAddress: clientIp
    endIpAddress: clientIp
  }
  dependsOn: [ allowAzure ]
}

var connectionString = 'Host=${server.properties.fullyQualifiedDomainName};Port=5432;Database=${databaseName};Username=${dbAdminUser};Password=${dbAdminPassword};SSL Mode=Require;Trust Server Certificate=true'

resource app 'Microsoft.App/containerApps@2024-03-01' = if (deployApp) {
  name: '${appName}-app'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${pullIdentity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
      registries: [
        {
          server: registry.properties.loginServer
          identity: pullIdentity.id
        }
      ]
      secrets: [
        {
          name: 'db-connection'
          value: connectionString
        }
        {
          name: 'invite-code'
          value: inviteCode
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'web'
          image: '${registry.properties.loginServer}/seed-planner:${imageTag}'
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            { name: 'ConnectionStrings__Default', secretRef: 'db-connection' }
            { name: 'Registration__InviteCode', secretRef: 'invite-code' }
            { name: 'Database__MigrateOnStartup', value: 'true' }
            // TLS ends at the Container Apps ingress; the app must trust X-Forwarded-Proto.
            { name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED', value: 'true' }
          ]
        }
      ]
      scale: {
        minReplicas: minReplicas
        // One replica only: login cookies are protected with a key that lives inside the container.
        maxReplicas: 1
      }
    }
  }
  dependsOn: [ acrPull, allowAzure ]
}

output registryName string = registry.name
output registryLoginServer string = registry.properties.loginServer
output databaseHost string = server.properties.fullyQualifiedDomainName
output appUrl string = deployApp ? 'https://${app!.properties.configuration.ingress.fqdn}' : ''

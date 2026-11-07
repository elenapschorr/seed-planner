# Azure deployment

`main.bicep` creates everything the app needs in one resource group:

| Resource | Purpose |
|---|---|
| Container Apps environment + Log Analytics | runs the app, collects logs |
| Container Registry (Basic) | stores the Docker image |
| PostgreSQL Flexible Server 17 (Burstable B1ms, 32 GB) + database `seedplanner` | data |
| Container app `seedplanner-app` | the web app (0.25 vCPU, 0.5 GiB, one replica) |
| Managed identity with `AcrPull` | lets the app pull its image without a registry password |

Secrets (database password, invitation code) are passed as parameters at deployment time and
stored as Container Apps secrets. They are never written to this repository.

## Prerequisites

- Azure CLI (`az`), logged in with `az login`, and a subscription
- Docker (to build and push the image)
- Choose a database password with **letters and digits only**, at least 16 characters
  (it is placed into a connection string).

## 1. Create the infrastructure

The registry has to exist before an image can be pushed, so the first run creates
everything except the container app.

```powershell
az group create --name rg-seedplanner --location germanywestcentral

az deployment group create `
  --resource-group rg-seedplanner `
  --template-file infra/main.bicep `
  --parameters dbAdminPassword='<password>' inviteCode='<invitation code>' clientIp='<your public IP>'
```

`clientIp` opens the database firewall for your computer, which you need for the data import.
Find your address with `curl https://api.ipify.org`.

If PostgreSQL Burstable is not available in the region, add `location=westeurope` (to the
group creation and to the parameters).

## 2. Build and push the image

```powershell
$acr = az deployment group show -g rg-seedplanner -n main --query properties.outputs.registryName.value -o tsv
az acr login --name $acr
docker build -t "$acr.azurecr.io/seed-planner:v1" .
docker push "$acr.azurecr.io/seed-planner:v1"
```

## 3. Create the container app

Run the deployment again with the app switched on:

```powershell
az deployment group create `
  --resource-group rg-seedplanner `
  --template-file infra/main.bicep `
  --parameters dbAdminPassword='<password>' inviteCode='<invitation code>' clientIp='<your public IP>' deployApp=true imageTag=v1
```

The output `appUrl` is the address of the site. On its first start the app creates the
tables itself (`Database__MigrateOnStartup=true`).

## 4. Register and import data

1. Open the site and register with the invitation code. This is your account in Azure.
2. Generate the SQL locally (see `docs/DEVELOPMENT.md`), first the shared data, then your plants:

   ```powershell
   python tools/import_seeds.py data/Samen.xlsx --mode reference --out data/import_reference.sql
   python tools/import_seeds.py data/Samen.xlsx --mode plants --owner-email you@example.com --out data/import_plants.sql
   ```
3. Run both files against the Azure database (a `postgres` image serves as psql client):

   ```powershell
   $dbhost = az deployment group show -g rg-seedplanner -n main --query properties.outputs.databaseHost.value -o tsv
   docker run --rm -i -e PGPASSWORD='<password>' postgres:17 psql "host=$dbhost dbname=seedplanner user=seedadmin sslmode=require" -v ON_ERROR_STOP=1 < data/import_reference.sql
   docker run --rm -i -e PGPASSWORD='<password>' postgres:17 psql "host=$dbhost dbname=seedplanner user=seedadmin sslmode=require" -v ON_ERROR_STOP=1 < data/import_plants.sql
   ```

For a demo account: register a second user with another e-mail address and import the plants
for that address the same way.

## Updating the app

Push a new tag and point the app at it:

```powershell
docker build -t "$acr.azurecr.io/seed-planner:v2" .
docker push "$acr.azurecr.io/seed-planner:v2"
az containerapp update -g rg-seedplanner -n seedplanner-app --image "$acr.azurecr.io/seed-planner:v2"
```

## Cost control

- The database is the main cost. Everything stops costing money when the resource group is deleted:

  ```powershell
  az group delete --name rg-seedplanner --yes
  ```

  Deleting also deletes the data. Create a backup first (`docs/DEVELOPMENT.md`).
- `minReplicas=0` lets the app scale to zero when nobody visits (the first request is slower).
- `az postgres flexible-server stop` pauses the database compute for up to seven days;
  storage is still billed.

## Notes and trade-offs

- **Database access:** the server is reachable over the public internet, protected by TLS,
  a password and a firewall (Azure services plus an optional single IP). A private network
  (VNet + private endpoint) would be the next step for production, at extra cost and complexity.
- **Certificate validation:** the connection string uses `Trust Server Certificate=true`.
  Traffic is encrypted, but the server certificate is not verified.
- **One replica:** the keys that protect login cookies live inside the container. After a
  restart users have to log in again, and several replicas would not share sessions.
  Persisting the keys (for example in blob storage) would remove this limit.

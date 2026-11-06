# ---- Stage 1: build with the .NET SDK ------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy only the project files first, so "dotnet restore" is cached as long as the packages do not change
COPY src/SeedPlanner.Domain/SeedPlanner.Domain.csproj src/SeedPlanner.Domain/
COPY src/SeedPlanner.Infrastructure/SeedPlanner.Infrastructure.csproj src/SeedPlanner.Infrastructure/
COPY src/SeedPlanner.Api/SeedPlanner.Api.csproj src/SeedPlanner.Api/
RUN dotnet restore src/SeedPlanner.Api/SeedPlanner.Api.csproj

# Then the source code
COPY src/ src/
RUN dotnet publish src/SeedPlanner.Api/SeedPlanner.Api.csproj -c Release -o /app --no-restore

# ---- Stage 2: small runtime image (no SDK) -------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Listen on port 8080 (TLS is handled by the platform in front of the container)
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# Do not run as root
USER $APP_UID

ENTRYPOINT ["dotnet", "SeedPlanner.Api.dll"]

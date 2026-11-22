# Development guide

All commands are run from the repository root (`seed-planner`) in PowerShell unless noted otherwise.

## Prerequisites

- .NET SDK 10
- Docker Desktop (virtualization must be enabled in the BIOS/UEFI)
- Python 3 with `openpyxl` (`pip install openpyxl`), only for the one-time seed import
- EF Core tools: `dotnet tool install --global dotnet-ef`
- Optional: DBeaver to browse the database

## Project layout

| Path | Content |
|---|---|
| `src/SeedPlanner.Domain` | Entities, enums, domain logic |
| `src/SeedPlanner.Infrastructure` | EF Core `DbContext`, migrations |
| `src/SeedPlanner.Api` | ASP.NET Core Razor Pages app (`Program.cs`) |
| `tests/SeedPlanner.Tests` | Unit tests |
| `tools/` | One-time import script and plant type name translations |
| `data/` | Local data (Excel, generated SQL and report). Not committed |

## 1. Start the database

Create `.env` from the template and choose a local password (letters and digits only):

```powershell
Copy-Item .env.example .env
# edit .env: POSTGRES_PASSWORD=...
docker compose up -d
docker compose ps        # status should be "healthy"
```

The database is PostgreSQL 17 on `localhost:5432`, data lives in the Docker volume `seedplanner-db`.

Stop it with `docker compose down`. **`docker compose down -v` also deletes the data volume.**

## 2. Connection string (user secrets)

The application reads `ConnectionStrings:Default`. Docker Compose reads `.env`, the application does not,
so the password must be set in both places and must be identical:

```powershell
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5432;Database=seedplanner;Username=seedplanner;Password=YOUR_PASSWORD" --project src/SeedPlanner.Api
```

Error `28P01 password authentication failed` means the two passwords differ.
In production the same setting is provided as environment variable `ConnectionStrings__Default`.

## 3. Database migrations

The schema version is stored in the table `__EFMigrationsHistory`. Migration files live in
`src/SeedPlanner.Infrastructure/Migrations` and are committed.

```powershell
# create a migration after changing entities or the DbContext
dotnet ef migrations add MigrationName --project src/SeedPlanner.Infrastructure --startup-project src/SeedPlanner.Api

# apply all pending migrations
dotnet ef database update --project src/SeedPlanner.Infrastructure --startup-project src/SeedPlanner.Api

# go back to an earlier migration (then remove the last migration file)
dotnet ef database update PreviousMigrationName --project src/SeedPlanner.Infrastructure --startup-project src/SeedPlanner.Api
dotnet ef migrations remove --project src/SeedPlanner.Infrastructure --startup-project src/SeedPlanner.Api
```

Check the generated migration before applying it (column types, foreign keys).

## 4. One-time import from Excel

The database stores English names only. `tools/plant_type_names.csv` (columns `german;english;russian;note`)
maps the German plant type names from the Excel file to English names and provides the translations
for the table `plant_type_translations` (languages `de`, `en`, `ru`).

Put the Excel file in `data/` (it is not committed), then:

```powershell
# 1. remove existing data (the import refuses to run on non-empty tables)
docker compose exec db psql -U seedplanner -d seedplanner -c "TRUNCATE plant_types RESTART IDENTITY CASCADE;"

# 2. generate data/import.sql and data/import_report.txt (does not touch the database)
python tools/import_seeds.py data/Samen.xlsx

# 3. copy the SQL into the container and run it
docker compose cp data/import.sql db:/tmp/import.sql
docker compose exec db psql -U seedplanner -d seedplanner -f /tmp/import.sql

# 4. check the counts
docker compose exec db psql -U seedplanner -d seedplanner -c "select (select count(*) from plant_types) t, (select count(*) from plant_type_translations) tr, (select count(*) from plants) p, (select count(*) from season_windows) w;"
```

Expected for the current Excel file: 101 plant types, 303 translations, 198 plants, 404 season windows.
The import runs in one transaction; if anything fails, nothing is written.
`data/import_report.txt` lists warnings and notes (for example plant types where the exposure of varieties differs).

## 5. Run the application

```powershell
dotnet run --project src/SeedPlanner.Api
```

## 6. Tests

```powershell
dotnet test
```

JavaScript of the garden page (Node.js 20 or newer, no packages needed):

```powershell
node --test tests/js/garden-geometry.test.js tests/js/garden-trace.test.js tests/js/garden-model.test.js tests/js/garden-history.test.js tests/js/page/page.test.js
```

`tests/js/page` runs the page script and all its modules against a small DOM mock (`mock.js`); every `*.check.js` file is one scenario.

## 7. Backup and restore

After the Excel file is retired, the database is the only copy of the data. Back it up regularly:

```powershell
# backup (custom format, compressed)
docker compose exec db pg_dump -U seedplanner -d seedplanner -Fc -f /tmp/seedplanner.dump
docker compose cp db:/tmp/seedplanner.dump backups/seedplanner-YYYY-MM-DD.dump

# restore into an empty database
docker compose cp backups/seedplanner-YYYY-MM-DD.dump db:/tmp/seedplanner.dump
docker compose exec db pg_restore -U seedplanner -d seedplanner --clean --if-exists /tmp/seedplanner.dump
```

Keep the `backups/` folder out of git (add it to `.gitignore`) and store copies outside this computer.

## Troubleshooting

| Message | Cause and fix |
|---|---|
| `failed to connect to the docker API` | Docker Desktop is not running. Start it. |
| `no configuration file provided: not found` | Wrong folder. Run Compose commands in the folder with `docker-compose.yml`. |
| `28P01 password authentication failed` | Password in user secrets differs from `.env`. Make them identical; if the volume was created with another password, `docker compose down -v` and start again. |
| `No project was found` (dotnet ef) | Use `--project` and `--startup-project` as shown above. |
| `Tables are not empty; import aborted` | Run the `TRUNCATE` step first. |

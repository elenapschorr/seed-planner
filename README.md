# Seed Planner

A web app to manage a private seed collection (formerly an Excel sheet) and to plan what to grow, where and when.
The goal of the collection is to reproduce seeds yourself, so the app knows which varieties are open-pollinated and which are F1 hybrids.

Portfolio project, all rights reserved.

## Features

- **Seed collection:** plant types, varieties, sowing and harvest windows, germination temperature, height and spacing, pot size needs
- **Containers and beds:** pot groups, single pots, raised beds (with optional cover), plantings in a pot or a bed
- **Demand and plans:** yearly need per person for each plant type, named plans per year (persons, min/middle/max, own numbers), shopping list for varieties that are not in stock
- **This month:** what to sow, sow indoors, plant out and harvest in a month, worked out from the plan and the sowing and harvest windows of the varieties
- **Distribution:** places the planned plants into the chosen pots and beds (size, height, good and bad neighbours, cold frames) and explains its decisions
- **Garden plot editor:** upload a plan of the plot, trace its outline from the lines in the picture, mark areas and neighbours, split by a line, place beds; undo with Ctrl+Z
- **Multi-user:** one shared database, every record belongs to an owner; registration only with an invitation code
- **Three languages:** German, English, Russian (browser language, switch DE/EN/RU in the layout); all data in the database is English

## Technology

ASP.NET Core Razor Pages, C# / .NET 10, EF Core with PostgreSQL 17, Docker Compose.
The garden editor is plain JavaScript without a framework (modules in `src/SeedPlanner.Api/wwwroot/js`).
Unit tests with xUnit (domain logic such as the plan calculator and the allocator) and Node.js tests for the editor. CI runs both.
An Azure deployment (`infra/`, Bicep) is prepared.

## Quick start

```powershell
Copy-Item .env.example .env        # choose a local password in .env
docker compose up -d db
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5432;Database=seedplanner;Username=seedplanner;Password=YOUR_PASSWORD" --project src/SeedPlanner.Api
dotnet run --project src/SeedPlanner.Api
```

The full setup (migrations, one-time Excel import, backup) is described in [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md).

## Tests

```powershell
dotnet test
node --test tests/js/garden-geometry.test.js tests/js/garden-trace.test.js tests/js/garden-model.test.js tests/js/garden-history.test.js tests/js/page/page.test.js
```

## Structure

| Path | Content |
|---|---|
| `src/SeedPlanner.Domain` | Entities, plan calculator, allocator |
| `src/SeedPlanner.Infrastructure` | EF Core `DbContext`, migrations |
| `src/SeedPlanner.Api` | Razor Pages app, localization, JavaScript |
| `tests` | xUnit and Node.js tests |
| `tools` | One-time import scripts and name translations |
| `infra` | Azure (Bicep) |

## License

© 2026 Elena Pschorr. All rights reserved.

This repository is a personal portfolio project. The source code is
published for viewing and evaluation only. No license is granted to
copy, modify, distribute or use it commercially.

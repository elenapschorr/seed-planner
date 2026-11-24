# Third-party components

Seed Planner itself is not licensed for reuse (see the README). The following third-party
components are bundled with it and keep their own licenses.

## Fonts (SIL Open Font License 1.1)

Both fonts are self-hosted in `src/SeedPlanner.Api/wwwroot/fonts/` (Latin and Cyrillic subsets in WOFF2 format, as
distributed by [Fontsource](https://fontsource.org)).

| Font | Copyright |
| --- | --- |
| Fraunces | Copyright 2018 The Fraunces Project Authors (https://github.com/undercasetype/Fraunces) |
| IBM Plex Sans | Copyright © 2017 IBM Corp. with Reserved Font Name "Plex" |

Both fonts are licensed under the SIL Open Font License, Version 1.1. The license texts ship with the fonts:
`src/SeedPlanner.Api/wwwroot/fonts/OFL-Fraunces.txt` and `src/SeedPlanner.Api/wwwroot/fonts/OFL-IBMPlex.txt`.
The official text and FAQ are at <https://openfontlicense.org>.

## JavaScript and CSS libraries (MIT)

Each library keeps its license file next to it in `src/SeedPlanner.Api/wwwroot/lib/`.

| Library | Version | License file |
| --- | --- | --- |
| Bootstrap | 5.3.3 | `lib/bootstrap/LICENSE` |
| jQuery | 3.7.1 | `lib/jquery/LICENSE.txt` |
| jQuery Validation | | `lib/jquery-validation/LICENSE.md` |
| jQuery Validation Unobtrusive | | `lib/jquery-validation-unobtrusive/LICENSE.txt` |

## NuGet packages

The .NET packages (ASP.NET Core, Entity Framework Core, Npgsql, EFCore.NamingConventions) are used as
dependencies through NuGet and are not part of this repository. See the `*.csproj` files for the list.

# Tasks

## 1. Domain factories accept a seeded identifier

- [x] 1.1 Add `Guid id` as the first parameter of `Station.Create`, `Platform.Create`, `Line.Create` and `Service.Create`, reject `Guid.Empty` with a new `<Entity>Errors.IdRequired`, and update the XML comment that currently promises a generated identifier; verify `dotnet build SmartRail.sln -warnaserror` is clean (no other call site exists — check with a search for `Station.Create`, `Platform.Create`, `Line.Create` and `Service.Create`), and `dotnet test tests/SmartRail.ArchitectureTests` still passes

## 2. Seed files

- [x] 2.1 Add `seed/network/network.json` (nine stations with their platforms, the two lines with their ordered station codes) and `seed/network/timetable.json` (52 services with their stops, the guard ID from `seed/identity/staff.json`, and the times in design D7), using fixed UUIDv7 identifiers; verify both files parse (`Get-Content seed/network/network.json | ConvertFrom-Json` and the same for the other file) and that a clean `dotnet build src/SmartRail.Host` copies them to `src/SmartRail.Host/bin/Debug/net10.0/seed/network/`

## 3. Validated seed model and its unit tests

- [x] 3.1 Add `Seeding/RailNetworkSeedReader.cs` (and its `RailNetworkSeed` record types) to `SmartRail.RailNetwork.Infrastructure`, reading both files with unmapped members disallowed and enforcing every rule in design D6 before returning a model that the seeder can persist; add `InternalsVisibleTo("SmartRail.RailNetwork.UnitTests")` to the Infrastructure project; verify `dotnet build SmartRail.sln -warnaserror` is clean
- [x] 3.2 Add the `tests/SmartRail.RailNetwork.UnitTests` project (Infrastructure plus Domain references, the existing test packages, the seed files copied to its output like the Host does) to `SmartRail.sln`, with the cases named in design D9; verify `dotnet test tests/SmartRail.RailNetwork.UnitTests` passes, then break one rule in a copy of each file (an unknown station ID; an arrival earlier than the previous departure) and confirm the failure message names the file and the 1-based record before reverting the copy

## 4. Seeder, start-up wiring and end-to-end check

- [x] 4.1 Add `RailNetworkSeeder : IModuleSeeder` in `Seeding/` (Schema = `RailNetworkDbContext.Schema`, insert-missing by the natural keys in design D3, one `SaveChangesAsync`, an `Information` log with the counts) and register it with one `services.AddScoped<IModuleSeeder, RailNetworkSeeder>();` line in `SmartRail.RailNetwork.Infrastructure/DependencyInjection.cs`; verify by running the application against an empty database (`docker compose down -v && docker compose up --build -d api`), calling `GET /api/v1/network/stations` (nine, one interchange), `GET /api/v1/network/lines` (two) and `GET /api/v1/timetable/journeys` (52 for today), then restarting the API and confirming the log reports zero inserts and the counts are unchanged
- [x] 4.2 Add `Seeding/NetworkSeedTests.cs` to `tests/SmartRail.Host.IntegrationTests` (start twice, assert the counts from SQL, assert the three endpoints with a bounded poll for journeys); verify `dotnet test tests/SmartRail.Host.IntegrationTests` passes in full, including the existing seeding, authorization, rate-limit, migration and live-operations tests without weakening any of them

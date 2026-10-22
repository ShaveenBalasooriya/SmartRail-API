# Implementation Plan: Seed the network and timetable

This is the build plan for `add-network-timetable-seed`. The specs are the source of truth; where this
plan and a spec disagree, the spec wins.

## Order of work

Four stacked PRs, each reviewable on its own and each green before the next starts.

| PR | Tasks | What lands | Why it is separate |
|---|---|---|---|
| 1 | 1.1 | The four domain factories take an ID | A Domain-only change with no behaviour yet; nothing calls the factories until PR 3 |
| 2 | 2.1 | The two seed files | Pure data; the reviewer reads names, order and times without code noise |
| 3 | 3.1, 3.2 | Reader, validator and its unit tests | The rules are testable without a database, so they land before any persistence |
| 4 | 4.1, 4.2 | Seeder, DI registration, integration test | The only PR that writes to PostgreSQL |

Nothing is committed or pushed by the drafting agent; the implementer commits through Graphite per
`AGENTS.md` §4.

## Files

**New**

| Path | Contents |
|---|---|
| `seed/network/network.json` | Nine stations with platforms, two lines with ordered station codes |
| `seed/network/timetable.json` | 52 services with their stops |
| `src/Modules/RailNetwork/SmartRail.RailNetwork.Infrastructure/Seeding/RailNetworkSeedReader.cs` | `internal static class` (or internal sealed class with a static `Read(string seedRoot)`): reads both files, validates, returns `RailNetworkSeed` |
| `src/Modules/RailNetwork/SmartRail.RailNetwork.Infrastructure/Seeding/RailNetworkSeed.cs` | The validated model: `StationSeed`, `PlatformSeed`, `LineSeed`, `ServiceSeed`, `ServiceStopSeed` records and the lookup dictionaries the seeder needs |
| `src/Modules/RailNetwork/SmartRail.RailNetwork.Infrastructure/Seeding/RailNetworkSeeder.cs` | `internal sealed class RailNetworkSeeder(...) : IModuleSeeder` |
| `tests/SmartRail.RailNetwork.UnitTests/SmartRail.RailNetwork.UnitTests.csproj` | Test project; Infrastructure + Domain references; seed files copied to output |
| `tests/SmartRail.RailNetwork.UnitTests/Seeding/RailNetworkSeedReaderTests.cs` | Validation and dataset tests |
| `tests/SmartRail.Host.IntegrationTests/Seeding/NetworkSeedTests.cs` | Start-twice and endpoint tests |

**Modified**

| Path | Change |
|---|---|
| `src/Modules/RailNetwork/SmartRail.RailNetwork.Domain/Entities/Station.cs` | `Create(Guid id, StationCode code, string name)`; reject `Guid.Empty`; comment no longer says "generated with `Guid.CreateVersion7`" |
| `.../Entities/Platform.cs` | `Create(Guid id, Guid stationId, PlatformNumber number)` |
| `.../Entities/Line.cs` | `Create(Guid id, LineCode code, string name)` |
| `.../Entities/Service.cs` | `Create(Guid id, Guid lineId, ServiceDirection direction, string name, Guid guardStaffId)` |
| `.../Entities/StationErrors.cs`, `PlatformErrors.cs`, `LineErrors.cs`, `ServiceErrors.cs` | One `IdRequired` error each, keyed `station.id`, `platform.id`, `line.id`, `service.id` |
| `src/Modules/RailNetwork/SmartRail.RailNetwork.Infrastructure/SmartRail.RailNetwork.Infrastructure.csproj` | `InternalsVisibleTo("SmartRail.RailNetwork.UnitTests")` |
| `src/Modules/RailNetwork/SmartRail.RailNetwork.Infrastructure/DependencyInjection.cs` | One line: `services.AddScoped<IModuleSeeder, RailNetworkSeeder>();` next to the other scoped registrations, plus the two usings it needs |
| `SmartRail.sln` | The new unit-test project |

## Seed file schema

### `seed/network/network.json`

```jsonc
{
  "stations": [
    {
      "id": "0199c3f1-…",        // required, UUIDv7, unique across both files
      "code": "CMB",             // required, unique, natural key
      "name": "Colombo Fort",    // required, non-blank
      "platforms": [             // required, at least one
        { "id": "0199c3f1-…", "number": "1" }   // number unique within the station
      ]
    }
  ],
  "lines": [
    {
      "id": "0199c3f1-…",        // required, UUIDv7, unique across both files
      "code": "COA",             // required, unique, natural key
      "name": "Coastal Line",    // required, non-blank
      "stations": ["CMB", "KLP", "BAM", "WEL", "MLV"]  // required, station codes, no repeats
    }
  ]
}
```

Example record — one station, one line:

```json
{
  "id": "0199c3f1-8a20-7c31-9d4e-6f1b2c3d4e50",
  "code": "CMB",
  "name": "Colombo Fort",
  "platforms": [
    { "id": "0199c3f1-8a20-7c31-9d4e-6f1b2c3d4e6a", "number": "1" },
    { "id": "0199c3f1-8a20-7c31-9d4e-6f1b2c3d4e6b", "number": "2" },
    { "id": "0199c3f1-8a20-7c31-9d4e-6f1b2c3d4e6c", "number": "3" }
  ]
}
```

```json
{
  "id": "0199c3f1-8a20-7c31-9d4e-6f1b2c3d4e80",
  "code": "COA",
  "name": "Coastal Line",
  "stations": ["CMB", "KLP", "BAM", "WEL", "MLV"]
}
```

### `seed/network/timetable.json`

```jsonc
{
  "services": [
    {
      "id": "0199c3f1-…",        // required, UUIDv7, unique across both files
      "lineId": "0199c3f1-…",    // required, a line in network.json
      "direction": "outbound",   // "outbound" | "inbound"
      "name": "Coastal 0610 Outbound",  // unique within (line, direction)
      "guardStaffId": "01a15874-e69d-73b2-b24d-d85d22fdead2",
      "stops": [                 // required, one per line station, in running order
        {
          "sequence": 1,                          // 1..n, contiguous
          "stationId": "0199c3f1-…",              // a station in network.json
          "platformId": "0199c3f1-…",             // a platform of that station
          "isCalling": true,
          "arrival": "06:16",                     // "HH:mm"; absent on the first stop
          "departure": "06:17"                    // "HH:mm"; absent on the last stop
        }
      ]
    }
  ]
}
```

Example service (truncated to two stops; a real service has five):

```json
{
  "id": "0199c3f1-8a20-7c31-9d4e-6f1b2c3d4f01",
  "lineId": "0199c3f1-8a20-7c31-9d4e-6f1b2c3d4e80",
  "direction": "outbound",
  "name": "Coastal 0610 Outbound",
  "guardStaffId": "01a15874-e69d-73b2-b24d-d85d22fdead2",
  "stops": [
    {
      "sequence": 1,
      "stationId": "0199c3f1-8a20-7c31-9d4e-6f1b2c3d4e50",
      "platformId": "0199c3f1-8a20-7c31-9d4e-6f1b2c3d4e6b",
      "isCalling": true,
      "departure": "06:10"
    },
    {
      "sequence": 2,
      "stationId": "0199c3f1-8a20-7c31-9d4e-6f1b2c3d4e51",
      "platformId": "0199c3f1-8a20-7c31-9d4e-6f1b2c3d4e71",
      "isCalling": true,
      "arrival": "06:16",
      "departure": "06:17"
    }
  ]
}
```

### Writing the files

- Generate the UUIDv7 values once (for example with a throwaway `dotnet script`, or `uuidgen` plus a
  version nibble) and keep them stable; never `Guid.NewGuid()`.
- Service names follow `<Line short name> <HHmm> <Direction>`, e.g. `Coastal 0010 Outbound`,
  `Main 0110 Outbound`, `Coastal 0040 Inbound`. The time in the name is the first departure, which
  makes a duplicate name impossible within a direction.
- Stop times come from the first departure plus the offsets `0, 6/7, 13/14, 20/21, 27`; inbound uses
  the reverse station order with the same offsets.
- 52 services × 5 stops = 260 stop records. The file is long by nature; keep one stop per line
  (compact JSON) so a diff is readable.

## Seeder algorithm

```
SeedAsync(cancellationToken):
    seedRoot = ResolveSeedRoot(configuration)              // Seed:Path, else AppContext.BaseDirectory/seed
    seed = RailNetworkSeedReader.Read(seedRoot)             // parses + validates both files; throws with file + index

    existingStationCodes  = database.Stations.Select(s => s.Code).ToHashSet()
    existingLineCodes     = database.Lines.Select(l => l.Code).ToHashSet()
    existingPlatformKeys  = database.Platforms.Select(p => new { p.StationId, p.Number }).ToHashSet()
    existingServiceKeys   = database.Services.Select(s => new { s.LineId, s.Direction, s.Name }).ToHashSet()

    stations  = seed.Stations.Where(s => !existingStationCodes.Contains(s.Code))      // with platforms
    lines     = seed.Lines.Where(l => !existingLineCodes.Contains(l.Code))           // with line stations
    services  = seed.Services.Where(s => !existingServiceKeys.Contains(key(s)))      // with stops

    database.Stations.AddRange(stations.Select(CreateStationWithPlatforms))
    database.Lines.AddRange(lines.Select(CreateLineWithLineStations))
    database.Services.AddRange(services.Select(CreateServiceWithStops))

    await database.SaveChangesAsync(cancellationToken)     // one transaction

    logger.LogInformation("Seeded schema {Schema}: {Stations} station(s), {Platforms} platform(s), " +
        "{Lines} line(s), {Services} service(s), {Stops} stop(s) added", ...)
```

Notes for the implementer:

- Resolve every reference through the validated model, never by re-parsing JSON in the persistence
  step. `RailNetworkSeed` should expose `StationById`, `LineById`, `PlatformById`, and each
  `ServiceSeed` should already hold resolved station/platform IDs.
- Read existing rows with `AsNoTracking()` and project them to plain values (`.Select(s => s.Code)
  .ToListAsync()`), then compare in memory. A `Where` on a value object such as `StationCode` depends
  on EF translating the converter and is not worth relying on.
- Platforms of an existing station are still checked individually by (`station`, `number`): a
  platform missing from the database is inserted even when its station exists.
- Call the domain factories (`Station.Create(id, …)`, `Platform.Create(id, …)`, `Line.Create(id, …)`,
  `LineStation.Create(lineId, stationId, sequence)`, `Service.Create(id, …)`, `ServiceStop.Create(…)`)
  and treat a failure as a seeder bug: the validator has already checked the same rules, so a failure
  here means the two disagree and must be thrown, not skipped.
- Do not add a `Seed:Enabled` flag, a retry, or a "force" mode.
- `TimeOnly` parsing: use `TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, …)` in
  the reader, so `6:10` and `06:10:00` fail rather than silently differ from the file.

## Validation error messages

One shape, so a start-up failure is actionable without reading the code:

```
Seed file '<absolute path>' record 12 ('Coastal 0610 Outbound') is invalid: <reason>
Seed file '<absolute path>' contains an unknown station reference '0199…' in service 'Coastal 0610 Outbound', stop 3.
Seed file '<absolute path>' was not found.
Seed file '<absolute path>' is not valid seed data: <JsonException message>
```

`<reason>` should be the domain factory's `Error.Description` where a factory produced it. Nothing is
logged with passenger data or secrets; there is none in this data.

## Validation rules (implement exactly these)

1. File exists, is readable, is JSON, has no unmapped property.
2. `stations`, `lines`, `services` present and non-empty.
3. Uniqueness: record IDs across both files; station codes; line codes; platform numbers within a
   station; station codes within a line; service (line, direction, name); stop sequence within a
   service.
4. References resolve: line station code → station; service `lineId` → line; stop `stationId` →
   station; stop `platformId` → platform; platform's station = stop's station.
5. Domain rules via the factories: non-blank code/name; station ID on a platform; line ID, direction,
   name, guard ID on a service; positive sequence; calling stop has a platform and at least one time;
   sequence > 1 calling stop has an arrival; non-calling stop has no times and no platform.
6. Service shape: stop count equals the line's station count; station order equals the line's order
   (outbound) or its reverse (inbound); sequences `1..n`; first stop has a departure and no arrival;
   last stop has an arrival and no departure; intermediate stops have both; times non-decreasing
   (previous departure ≤ this arrival ≤ this departure, and the first departure < the last arrival).

## Test list

`tests/SmartRail.RailNetwork.UnitTests/Seeding/RailNetworkSeedReaderTests.cs`

| Test | Asserts |
|---|---|
| `The_checked_in_seed_files_are_valid` | Both real files pass every rule |
| `An_unknown_station_reference_names_the_file_and_record` | Message contains the file name and the service name |
| `Times_that_go_backwards_are_rejected` | A service crossing midnight is refused |
| `A_duplicate_station_code_is_rejected` | Duplicate natural key refused |
| `The_dataset_is_two_lines_of_five_with_one_interchange` | 2 lines, 5 each, 9 distinct, 1 shared |
| `Every_seeded_service_has_the_lines_stations_in_direction_order` | Order, reversal, and that every stop is calling |
| `At_any_local_time_a_service_is_running_or_departs_within_an_hour` | All 1,440 minutes |

Broken inputs are built by copying a real file to a temp directory, editing the copy, and pointing the
reader at that root — never by mutating the checked-in file.

`tests/SmartRail.Host.IntegrationTests/Seeding/NetworkSeedTests.cs`

| Test | Asserts |
|---|---|
| `Starting_twice_leaves_one_set_of_network_and_timetable_rows` | SQL counts after two starts: 2 lines, 9 stations, 11 platforms, 52 services, 260 stops |
| `The_seeded_network_and_journeys_are_readable_through_the_public_api` | `GET /network/stations` (9, one interchange), `/network/lines` (2 × 5), `/timetable/journeys` (52, polling up to 10 s) |

## Risks during implementation

- **The 27-minute trip plus the 23:10 last departure** leaves 50 minutes before midnight. If a station
  or dwell is added, the last arrival moves; keep the total under ~50 minutes or validation will reject
  the late services. The unit test is what tells you.
- **Adding the extra peak departure** shifts a gap but can never create one; if departures are
  re-timed, re-run the coverage test rather than reasoning about it.
- **The `network` schema is shared with journey generation.** Never delete or "reset" rows from a
  seeder; insert only. A reset is an operator action (`docker compose down -v`).
- **Existing integration tests share one container database.** Seeding is idempotent, so repeated
  factory starts are safe, but do not add a test that truncates `network.*` — it would break every
  other class in the collection.
- **`TreatWarningsAsErrors`** is on. XML doc comments are not required (`GenerateDocumentationFile`
  is false), but unused usings and nullable warnings are errors.
- **Do not add a migration** for a unique index on `services`, even though the natural key would
  benefit. It collides with `add-event-bus` task 2.3, which adds the `network` outbox migration from
  the same snapshot. Recorded as a risk in the design instead.

## What the implementer must NOT touch

- `openspec/changes/add-event-bus/**` — another thread owns it. Do not edit its tasks, specs or design.
- `src/SmartRail.SharedKernel.Infrastructure/**`, any outbox entity, dispatcher, transport or
  `IOutboxSource` — same reason.
- `src/Modules/RailNetwork/SmartRail.RailNetwork.Application/Journeys/GenerateJourneys/**` — task 2.3
  of `add-event-bus` edits the handler; this change needs no change there.
- Any `Migrations/` folder or `RailNetworkDbContextModelSnapshot.cs` — no schema change belongs here.
- `src/Modules/IdentityAccess/**` and `seed/identity/**` — one guard is assigned, no identity edit.
- `src/SmartRail.Host/Program.cs`, `compose.yaml`, `Dockerfile`, `appsettings*.json` — the existing
  seed copy item and default `Seed:Path` already cover the new files. If a path problem appears, fix it
  in the seeder's resolver, not in the deployment files.
- The existing integration tests. If one fails because seeding changed its assumptions, report it and
  fix the seeder or the seed data, never by relaxing the test.

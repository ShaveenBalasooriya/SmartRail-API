# Design: Seed the network and timetable

## Context

EP-04 splits into two changes: `add-rail-network-timetable` built the tables, the daily generation
job and the read endpoints, and explicitly left loading out ("there is no seeding or loader in this
change, and tests insert their own rows"). This change supplies that half.

What is already in place and constrains the design:

- Rail Network owns **one** schema, `network`, and one `RailNetworkDbContext`. Lines, stations,
  platforms, `line_stations`, services, `service_stops` and journeys are all in it, so there is no
  cross-module ordering problem between "network" and "timetable" and no second seeder.
- `IModuleSeeder` (Shared Kernel Application) is the seam: a module implements it in Infrastructure,
  and `DatabaseSeeder` runs every registered seeder once after all migrations, in schema order,
  before the host serves traffic. A failure stops start-up.
- `IdentitySeeder` is the working pattern: `Seed:Path` root, JSON with unmapped members disallowed, a
  whole-file parse before any insert, one `SaveChanges`, an `Information` log with counts, and an
  `InvalidOperationException` naming the file and record index.
- `Program.cs` is migrate → seed → `app.Run()`. `JourneyGenerationJob` is a hosted service, so its
  first run happens after seeding. That is the whole reason journeys are not seeded.
- The only seeded guard is `guard.ishara`, `01a15874-e69d-73b2-b24d-d85d22fdead2` in
  `seed/identity/staff.json`. `services.guard_staff_id` is a plain column with no cross-schema FK.

## Goals / Non-Goals

**Goals**

- A clean database becomes the MVP 1 railway on `docker compose up`, with no manual step.
- The seed is reviewable as data: names, order, times and platforms are read in the file, not in code.
- A bad file stops start-up with a message that says which file and which record, and leaves nothing
  half-committed.
- Running the application twice changes nothing.

**Non-Goals**

- Editing the network or timetable at runtime, a write endpoint, or a Scheduler (EP-04 Out).
- Updating an existing record from a file (D5).
- A production data source, more guards, running-day calendars, midnight crossing, skipped stops.

## Decisions

### D1. A start-up JSON seed file (owner decision, 2026-10-22)

The owner accepted loading the network and timetable from a checked-in JSON file at start-up. It was
recommended because **MVP 1 has no write endpoint**: the only way data could otherwise get in is a
manual step that Postman or `psql` performs, which nothing enforces and CI cannot repeat.

Alternatives considered and rejected:

| Alternative | Why not |
|---|---|
| Admin write endpoint (`POST /network/stations`, …) | There is no Scheduler role in MVP 1 (EP-04 Out), so it would need a role that does not exist, plus validation, auditing and tests for a surface the scope defers. It also makes the network mutable at runtime, which the scope forbids ("no interface edits the network or the timetable in MVP 1") |
| EF Core `HasData` inside a migration | Puts data in schema history: every timetable edit becomes a migration, the data is not reviewable as data, and it bypasses the domain factories so none of the timetable rules are checked. Correcting a row also means a second migration |
| A SQL script run by Compose or by hand | It is not run by the application, so the local stack, the integration tests and production would each need their own invocation. It bypasses the domain rules and cannot express "insert only what is missing" as clearly as the seeder |
| C# constants in a `SeedData` class | Data mixed into code, large diffs in review, and no separation between what the railway is and how it is loaded |

This is the only decision in this change that came from the owner. Everything else below is the
orchestrator's design proposal, accepted here after checking it against the code; where the proposal
did not survive checking, the correction is stated.

### D2. One seeder, two files

`RailNetworkSeeder : IModuleSeeder` with `Schema => RailNetworkDbContext.Schema` in
`SmartRail.RailNetwork.Infrastructure/Seeding/`, reading:

- `seed/network/network.json` — stations with their platforms, and lines with their ordered stations.
- `seed/network/timetable.json` — services with their stops.

One seeder, not two, because the timetable cannot be validated without the network (every stop names
a station and a platform) and both write the same `DbContext`. A separate timetable seeder would have
to either duplicate the network lookup or rely on seeder ordering *inside* one schema, which
`DatabaseSeeder` does not express (it orders by schema name only). Two files, not one, because the
network changes rarely and the timetable changes often, so review diffs stay readable.

Both files are read and fully validated **before** the first `Add`. Persistence is one
`SaveChangesAsync`, which EF Core wraps in one transaction, so a domain failure on the last service
cannot leave the earlier ones committed. Insert order is stations → platforms → lines → line stations
→ services → stops; EF Core also orders by foreign keys inside the single save.

### D3. Stable IDs in the files, natural keys for idempotency, and the factory change

Every record in `network.json` carries an `id`, and `timetable.json` refers to lines, stations and
platforms by those IDs. Ids are the reference because **journeys, live state and the copies made for
the provider point at a service or a station by ID**: if a seed ID changed between starts, the rows
created from the old one would be orphaned. A code or a name can be corrected without breaking
anything; an ID cannot, which is exactly the property a reference needs.

Natural keys still decide *whether to insert*:

| Record | Natural key | Backed by |
|---|---|---|
| Station | `code` | unique index `stations(code)` |
| Platform | (`station`, `number`) | unique index `platforms(station_id, number)` |
| Line | `code` | unique index `lines(code)` |
| Service | (`line`, `direction`, `name`) | **no index** — checked in memory (see risks) |
| LineStation | (`line`, `station`) | unique index `line_stations(line_id, station_id)`; inserted only with its line |
| ServiceStop | (`service`, `sequence`) | unique index `service_stops(service_id, sequence)`; inserted only with its service |

So the files carry both: the ID for references, the natural key for identity. A record missing from
the database is inserted with the file's ID; a record already present by natural key is left alone,
even if its stored ID differs from the file's. References are resolved in two steps — file ID → file
record → natural key → stored row — so a stale ID in the file can never attach a stop to the wrong
station.

**The domain change.** `Station.Create`, `Platform.Create`, `Line.Create` and `Service.Create`
generate their own `Guid.CreateVersion7()` and take no ID, so a seeder cannot supply one. The smallest
change is to add `Guid id` as the first parameter of those four factories and reject `Guid.Empty` with
a new `<Entity>Errors.IdRequired`, exactly as `StaffMember.Create(Guid id, …)` already works for the
identity seed. `LineStation` and `ServiceStop` keep generating their own IDs because nothing outside
their parent refers to them, and their (parent, sequence) key is how the file identifies them.

Adding an overload instead (keep the generating factory, add an ID-taking one) was rejected: nothing
in the repository needs a generated ID for these four entities — a search finds **no call sites at
all** today — and two ways to create one entity invites drift. The seed's IDs are UUIDv7 values
generated once when the file is authored, so the repository rule "identifiers are version 7, never
`Guid.NewGuid()`" still holds for the data itself.

### D4. File shapes

Both files are camelCase JSON. The reader uses `JsonSerializerDefaults.Web` with
`JsonUnmappedMemberHandling.Disallow`, so a misspelt property is an error, not a silently ignored
field.

`network.json` — an object with `stations` and `lines`. A station owns its platforms; a line lists
station **codes** in outbound order, because the order is the one thing a reviewer must read at a
glance:

```json
{
  "stations": [
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
  ],
  "lines": [
    {
      "id": "0199c3f1-8a20-7c31-9d4e-6f1b2c3d4e80",
      "code": "COA",
      "name": "Coastal Line",
      "stations": ["CMB", "KLP", "BAM", "WEL", "MLV"]
    }
  ]
}
```

`timetable.json` — an object with `services`; each service owns its stops in running order:

```json
{
  "services": [
    {
      "id": "0199c3f1-8a20-7c31-9d4e-6f1b2c3d4f01",
      "lineId": "0199c3f1-8a20-7c31-9d4e-6f1b2c3d4e80",
      "direction": "outbound",
      "name": "Coastal 0610 Outbound",
      "guardStaffId": "01a15874-e69d-73b2-b24d-d85d22fdead2",
      "stops": [
        { "sequence": 1, "stationId": "0199c3f1-8a20-7c31-9d4e-6f1b2c3d4e50", "platformId": "0199c3f1-8a20-7c31-9d4e-6f1b2c3d4e6b", "isCalling": true, "departure": "06:10" },
        { "sequence": 2, "stationId": "0199c3f1-8a20-7c31-9d4e-6f1b2c3d4e51", "platformId": "0199c3f1-8a20-7c31-9d4e-6f1b2c3d4e71", "isCalling": true, "arrival": "06:16", "departure": "06:17" }
      ]
    }
  ]
}
```

Times are `HH:mm` strings parsed with `TimeOnly`, so a typo fails loudly instead of deserialising to
a wrong instant. `direction` is the stored string `outbound`/`inbound`. Both optional time fields are
absent rather than `null` when they do not apply, which is the only shape the `ServiceStop` rules
allow. The example above is truncated at two stops to stay readable; the real services have five.

### D5. Idempotency: insert missing only, never update

Per start, for each record type the seeder loads the existing natural keys into a set, then inserts
the file records that are absent. A line is inserted with its `line_stations`, and a service with its
stops, only if the line or service itself is missing; an existing line does not gain a newly added
station in its order, and an existing service's stops are never touched. That keeps the rule simple and
one-directional: **the file adds rows, it never rewrites them.** The log line at `Information` reports
how many of each were added, so a start that added nothing is visible.

The consequence is stated in one place rather than discovered later: **to apply a change to existing
seed data, reset the database** — `docker compose down -v` (which drops the `api-db-data` volume) and
start again. That discards everything derived from the seed too: `network.journeys`, the `operations`
live state and events built from them, and any catering or audit rows created since. In Development
that is the normal loop; nothing in MVP 1 is worth preserving.

An ID that differs between file and database is not logged as a warning. Because references resolve
through natural keys (D3), the difference changes nothing the application can observe, and no one can
act on the message. Reporting it would be noise on every start after any ID edit, which is exactly the
"extra hardening" `openspec/config.yaml` asks to leave out.

### D6. Fail-fast validation

Chosen deliberately over "skip bad records": a timetable that quietly loses a service produces a
believable but wrong board, and the operator has no way to notice.

The reader, in this order, throws `InvalidOperationException` naming the file (absolute path) and the
1-based record position:

1. File missing, unreadable, not JSON, or containing an unmapped property.
2. `stations`, `lines` or `services` missing or empty.
3. Duplicate record IDs across both files; duplicate station codes; duplicate line codes; duplicate
   platform numbers within a station; duplicate station inside a line's order; duplicate service
   natural key; duplicate stop sequence inside a service.
4. Unknown references: a line's station code, a service's `lineId`, a stop's `stationId` or
   `platformId`; a platform whose owning station is not the stop's station.
5. Domain rules, surfaced from the factories' `Result.Error.Description`: blank code or name,
   non-positive sequence, a calling stop without a platform or without a time, a second-or-later
   calling stop without an arrival, a non-calling stop carrying times or a platform, an empty guard
   ID, an unknown direction.
6. Dataset rules that the domain cannot see: stop count and station order must match the line and
   direction, sequences 1..n with no gaps, first stop has a departure and no arrival, last stop has an
   arrival and no departure, times non-decreasing along the stops (which is also the no-midnight-crossing
   rule: a service that wrapped would show a later stop with an earlier time).

No check that the guard exists in `identity`: there is no cross-schema FK and no contract that would
make it a network concern (data-model.md). A wrong guard ID is a data decision, not a seeding error.

What is **not** validated at runtime: "exactly two lines of five stations", "every stop is calling",
the 52-service count and the coverage rule. The model is meant to support more lines later (scope,
"the domain model supports more lines"), so hard-coding the MVP 1 shape into the seeder would be
wrong. Those are properties of the checked-in dataset and are asserted by unit tests over the real
files (D9), which fail CI if someone edits the data into an unrunnable shape.

### D7. The dataset (placeholder; the owner confirms names and timetable)

Scope fixes the shape: two lines, five stations each, one shared interchange, nine distinct stations,
both directions, every service every day. The owner decides the names and the timetable
(mvp-1-scope.md), so everything below is a **placeholder proposal** for the owner to confirm or
replace, chosen to be believable rather than asserted as fact. Railway time is `Asia/Colombo`
(`Railway:TimeZone` default), so "today" is the Colombo day.

| Line | Code | Stations in outbound order |
|---|---|---|
| Coastal Line | `COA` | `CMB` Colombo Fort · `KLP` Kollupitiya · `BAM` Bambalapitiya · `WEL` Wellawatte · `MLV` Mount Lavinia |
| Main Line | `MAI` | `CMB` Colombo Fort · `MAR` Maradana · `DMT` Dematagoda · `KLN` Kelaniya · `RGM` Ragama |

`CMB` is the interchange and appears in both orders, so 4 + 4 + 1 = 9 stations. Every station has one
numbered platform (`1`) except `CMB`, which has three (`1`, `2`, `3`) because two lines meet there:
11 platforms in total. All stops are calling stops: skipped stops are a deferred feature (`M-16`), and
seeding an example of them would imply behaviour nothing else implements yet.

Journey time is 27 minutes on both lines, at 6-minute hops with a 1-minute dwell (`0, 6/7, 13/14,
20/21, 27`). At the interchange, outbound services call at platform 2 and inbound at platform 3; every
other stop uses platform 1. That gives the platform reference more than one value without inventing a
platform-allocation rule.

**Services: 52 across the four line-directions (13 each).** The pattern is a clockface:

- Line `COA` departs on even hours, line `MAI` on odd hours, so the two lines interleave.
- Inbound departs 30 minutes after outbound on the same parity (`:10` outbound, `:40` inbound), so the
  network sees a departure every 30 minutes around the clock.
- Both outbound directions gain one **morning-peak** departure and both inbound directions one
  **evening-peak** departure, which makes each direction hourly through its peak and two-hourly
  elsewhere. Morning traffic runs out of Colombo Fort, evening traffic back into it, which is the
  believable direction for commuter flow.
- No departure later than 23:10 and none earlier than 00:10, because the trip is 27 minutes: the last
  service of the day arrives at 23:37 and no service crosses midnight.

Concretely, first departures per direction:

| Direction | Departures |
|---|---|
| `COA` outbound | 00:10 02:10 04:10 06:10 **07:10** 08:10 10:10 12:10 14:10 16:10 18:10 20:10 22:10 |
| `COA` inbound | 00:40 02:40 04:40 06:40 **07:40** 08:40 10:40 12:40 14:40 16:40 18:40 20:40 22:40 |
| `MAI` outbound | 01:10 03:10 05:10 **06:10** 07:10 09:10 11:10 13:10 15:10 17:10 19:10 21:10 23:10 |
| `MAI` inbound | 01:40 03:40 05:40 07:40 09:40 11:40 13:40 15:40 **16:40** 17:40 19:40 21:40 22:40 |

Bold is the peak extra. Sorted network-wide, consecutive first departures are never more than 60
minutes apart (the widest gap is 23:10 → 00:10), and every service is 27 minutes long, so at any local
time one of "a service is running" or "a service departs within 60 minutes" holds. That is the
coverage rule in the spec; the unit test recomputes it from the file rather than trusting this table.

**Why 52 and not more.** A believable commuter frequency — every 15 minutes per direction, all day —
is 4 directions × 96 departures = 384 services and about 1,900 stop records. That is a data dump no
reviewer reads, and it hides mistakes. At 52 the file is about 600 lines, still a real timetable, and
the board is never empty because the *network* is dense even though each direction is two-hourly
off-peak. The owner can raise the frequency by editing the file; nothing in the code assumes 52.

**Guard.** Every service names `01a15874-e69d-73b2-b24d-d85d22fdead2` (`guard.ishara`), because it is
the only guard in `seed/identity/staff.json`. One guard working every service is not realistic
operations, but adding guards means editing the identity seed and the Authentik blueprint, which is a
different change and an owner decision (Open decisions).

### D8. Environments

There is no `Seed:Enabled` flag, and this change does not add one. The identity seeder has only
`Seed:Path` (default: `seed` relative to `AppContext.BaseDirectory`), a missing file throws, and
seeding runs identically in Development, E2E and Production. Adding a switch would create a
configuration in which the application starts and serves an empty railway, which is the failure the
seeder exists to prevent. The behaviour is honest about what the data is: demo data for MVP 1. A
production timetable source is out of scope and an owner decision to schedule.

The path resolver in `IdentitySeeder` (about ten lines) is duplicated in the network seeder instead of
being lifted into the Shared Kernel. A shared `SeedPath` helper would mean editing
`SmartRail.SharedKernel*` while `add-event-bus` is working in
`SmartRail.SharedKernel.Infrastructure`, and ten lines of path handling is not worth that collision.

### D9. Tests and the e2e stack

Tests follow `openspec/config.yaml`: only where a rule is easy to break silently.

**Unit — new `tests/SmartRail.RailNetwork.UnitTests`** (project added to `SmartRail.sln`; CI already
runs `dotnet test SmartRail.sln`). It references `SmartRail.RailNetwork.Infrastructure` and reads the
real seed files, copied to its output with the same `../../seed/**/*.json` item the Host uses. The
reader/validator is `internal` and Infrastructure gets
`InternalsVisibleTo("SmartRail.RailNetwork.UnitTests")`; that is the one addition the project needs,
and no architecture rule forbids a test project referencing Infrastructure (`DependencyRuleTests`
only inspects `src/`).

Cases, not one per rule but one per failure mode that matters:

- `The_checked_in_seed_files_are_valid` — the reader accepts both real files (guards every rule).
- `An_unknown_station_reference_names_the_file_and_record` — one broken copy, asserting the message.
- `Times_that_go_backwards_are_rejected` — the midnight-crossing guard.
- `A_duplicate_station_code_is_rejected`.
- `The_dataset_is_two_lines_of_five_with_one_interchange`.
- `Every_seeded_service_has_the_lines_stations_in_direction_order`.
- `At_any_local_time_a_service_is_running_or_departs_within_an_hour` — the coverage rule, recomputed
  from the file for all 1,440 minutes.

**Integration — `tests/SmartRail.Host.IntegrationTests/Seeding/NetworkSeedTests.cs`**, same
`PostgresFixture` and `SmartRailApiFactory` as `IdentitySeedTests`:

- Start twice; assert the counts (2 lines, 9 stations, 11 platforms, 52 services, 260 stops) from SQL,
  so a second start proves idempotency. 260 = 52 × 5.
- `GET /api/v1/network/stations` → 9 with exactly one `isInterchange: true`; `GET
  /api/v1/network/lines` → 2 with 5 stations each; `GET /api/v1/timetable/journeys` (no date, so today
  in railway time) → 52 journeys. These endpoints are `.AllowAnonymousPublic()`, so the plain test
  client works; no token is needed.
- The journeys assertion polls the endpoint for up to ~10 seconds: generation is a hosted service that
  starts at `app.Run()`, so it is not finished when `/health/ready` first answers. This is stated in
  the test, not hidden behind a fixed `Task.Delay`.

Not tested: the seeder's insert path against a real DB for a *modified* file (there is none by
design), the log text, and the JSON schema itself.

**Existing tests.** I read every integration test that touches these paths: none inserts network rows
and none asserts that the network tables are empty. `StaffAuthorizationTests` (anonymous access) and
`PublicRateLimitTests` assert status codes only, so non-empty bodies change nothing;
`OpenApiDocumentTests` and `MigrationTests` read the document and `information_schema`;
`JourneyFactSinkTests` publishes its own `JourneyGenerated` and counts only its own journey's events.
The shared container database is seeded on the first factory start and idempotently re-seeded after
that, so all of them still see the data they expect. Task 4.2 requires the full suite to pass and
forbids weakening a test to get there.

**E2E stack.** No wiring is needed: `SmartRail.Host.csproj` already copies `../../seed/**/*.json` to
the output and publish directories, `.dockerignore` does not exclude `seed/`, and the `Dockerfile`
copies the whole context before `dotnet publish`, so `seed/network/*.json` lands in `/app/seed/network/`
and the default `Seed:Path` resolves there. Verified rather than assumed; the plan's last task checks
it on a fresh volume.

### D10. Interaction with journey generation, live state and `add-event-bus`

Seeding runs before `app.Run()`, so `JourneyGenerationJob`'s first run (a hosted service) always sees
the services. The job creates today's and tomorrow's journeys, and `JourneyGenerated` gives Live
Operations a live state per journey. No Live Operations or operations data is seeded; it is derived.

Today `JourneyGenerated` is published through MediatR after the journey save. `add-event-bus` task 2.3
will move it to an outbox row in the `network` schema, in the same transaction as the inserted
journeys. This change works either way and **depends on neither**: it only adds rows the job reads. Its
only overlap with that change is one line in `RailNetwork.Infrastructure/DependencyInjection.cs`
(`services.AddScoped<IModuleSeeder, RailNetworkSeeder>()`), which is a different line from anything
`add-event-bus` edits there, plus the absence of a migration.

One honest limit: the board is non-empty because journeys exist and are *scheduled or upcoming*, not
because anything is "running". A journey becomes running only when departure facts arrive
(`add-departure-logging`). The coverage rule is about the timetable being present at every hour, not
about faking live state.

## Risks / Trade-offs

- **[`services` has no unique index on (line, direction, name)]** The seeder checks the natural key in
  memory, which is enough while it is the only writer. A second writer (an endpoint or a job) could
  create a duplicate that seeding would then ignore. Adding the index needs a migration, which
  collides with `add-event-bus`'s `network` outbox migration; deferred deliberately, recorded here.
- **[Changing seed data needs a database reset]** Documented in D5, including what is lost. A
  contributor who edits a time and sees no change will otherwise think the seeder is broken.
- **[52 services create 104 journeys and 104 live states on first start]** Every integration test
  factory start triggers generation once per container. It is idempotent, so the cost is paid once;
  start-up gets a few seconds slower. Acceptable, and noted so nobody diagnoses it as a leak.
- **[Fixed IDs in a file]** If a station is deleted and re-added with a new ID while the old row
  survives, the database keeps both (different codes would be needed) — but that is a data edit
  without a reset, already covered by D5. References still resolve by natural key, so the failure mode
  is a stale row, not a broken stop.
- **[Placeholder data may be rejected]** The owner may want different names or frequencies; the cost
  is editing two JSON files and the three count assertions, not a design change.
- **[Coverage is a dataset property, not a runtime check]** A bad edit is caught by CI's unit test,
  not by start-up. That is the intended split (D6), but it means the test must not be deleted when the
  counts change.
- **[Demo data in Production]** Seeding runs everywhere (D8). A deployment that expects a real
  timetable must replace the files; that source does not exist yet and is on the owner's list.
- **[Readiness does not wait for journeys]** `/health/ready` can answer a fraction of a second before
  the first generation run finishes. The integration test polls for that reason. Not worth a new
  readiness condition.

## Open Decisions for the Owner

None of these blocks implementation; each has a default already in place.

1. **Station and line names (and the timetable itself).** The nine stations above are placeholders
   chosen to look like Colombo's suburban lines. *Recommendation:* accept them for the assessment and
   rename before any demo; the file is the only place to change them.
2. **How many guards are seeded.** Every service is assigned `guard.ishara`, the only seeded guard.
   *Recommendation:* keep one guard in this change, because a second guard means editing
   `seed/identity/staff.json` and the Authentik blueprint (`deploy/authentik/blueprints/smartrail.yaml`)
   and belongs in the identity change, not here. More guards is a nicer demo of "only my journeys",
   so it is worth doing before the Guard app is demonstrated.
3. **Where the real timetable comes from.** MVP 1 runs on checked-in demo data. *Recommendation:*
   defer a production source (an import or a feed) to MVP 2 and keep the file as the single source
   until then.

# Proposal: Seed the network and timetable

## Why

EP-04 says the network and its timetable are loaded as seed data, and that today's journeys are
generated from them (mvp-1-scope.md "The MVP 1 network and timetable", owner decision 2026-10-19).
`add-rail-network-timetable` built the tables, the generation job and the read endpoints but
deliberately left loading out: its proposal puts "seeding or any way of loading network or timetable
data" out of scope and its models start empty. Until something loads the two lines, the nine
stations and the timetable, the public endpoints answer with empty lists, journey generation does
nothing, Live Operations has no journey to track, and every later channel has nothing to show.
This change closes that gap with a start-up seed file, so a clean `docker compose up` produces the
MVP 1 railway on its own.

Delivers the seed-data half of **EP-04**. Supports PAX-02, PAX-03, PAX-07 (departures, calling stops
and the interchange), GRD-01 (assigned journeys have stops), CAT-02 and CAT-03 (stations, platforms
and journey data for the provider) and NC-01 (a live view with journeys in it).

## What Changes

- Two reviewable JSON files in `seed/network/`: `network.json` (nine stations with their platforms,
  two lines with their ordered stations) and `timetable.json` (52 services with their stops, times,
  platforms and the assigned guard).
- One `RailNetworkSeeder : IModuleSeeder` that reads both files, validates the whole dataset before
  touching the database, and inserts what is missing in a single transaction. Both files live in the
  one `network` schema, so there is no cross-module ordering problem.
- Four domain factories (`Station`, `Platform`, `Line`, `Service`) accept the identifier from the
  file instead of generating one, so the timetable and later data can reference stable IDs. The
  remaining two entities (`LineStation`, `ServiceStop`) keep generated IDs because nothing outside
  their parent refers to them.
- Startup behaviour is unchanged in shape: migrations, then seeders, then the host runs, so the
  existing journey-generation job creates today's and tomorrow's journeys from the seeded services.

## Capabilities

### New Capabilities

- `network-timetable-seed`: what the network and timetable seed files contain, how they are
  validated, and how they are inserted at start-up.

### Modified Capabilities

None. The generic start-up, idempotency and fail-fast rules are already specified by
`add-identity-access` `seed-data`; this capability adds the obligations that are specific to the
network and timetable, and repeats the idempotency promise for this data so the change reads alone.

## Out of Scope

- Any endpoint, screen or Scheduler tool that creates or edits network or timetable data (EP-04 Out;
  the Scheduler is deferred). The files are the only way in.
- Changing a seeded record by editing the file: seeding inserts what is missing and never updates.
  Re-seeding changed data means resetting the database (design D5).
- Journeys in the seed file. They are generated for today and tomorrow by the existing job, so
  their dates stay relative to the clock.
- Live Operations data. Live state is created from `JourneyGenerated` after seeding, not seeded.
- Running-day calendars, services that cross midnight, skipped (non-calling) stops in the dataset,
  station accessibility data, track segments between stations — the model has none of these and this
  change does not invent them.
- More seeded guards. One guard (`guard.ishara`) is assigned to every service; adding guards touches
  the identity seed and the Authentik blueprint and is left to the owner (design D7, Open decisions).
- A production timetable source (a real feed or import). MVP 1 runs on the checked-in demo data.
- Any migration. The four factory signatures change without a schema change, and a migration here
  would collide with the `network` outbox migration that `add-event-bus` adds.

## Impact

- `seed/network/network.json`, `seed/network/timetable.json` (new; copied to the output by the
  existing `../../seed/**/*.json` item in `SmartRail.Host.csproj`).
- `SmartRail.RailNetwork.Infrastructure/Seeding/` (new: reader/validator and seeder) and one
  registration line in its `DependencyInjection.cs`.
- `SmartRail.RailNetwork.Domain`: the four factory signatures and four `IdRequired` errors.
- `tests/SmartRail.RailNetwork.UnitTests` (new project) and one integration test class.
- No schema change, no endpoint change, no change to journey generation.

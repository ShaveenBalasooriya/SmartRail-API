# Spec Delta

## Purpose

Loads the network and the timetable that MVP 1 runs on from files kept in the repository, because they
are seed data and no interface edits them (EP-04; mvp-1-scope.md "The MVP 1 network and timetable";
owner decision 2026-10-19). Today's operational journeys are then generated from the seeded services;
they are not part of the seed.

## ADDED Requirements

### Requirement: Network and timetable loaded at start-up
The system SHALL load the network and the timetable from `seed/network/network.json` and
`seed/network/timetable.json` on every start, after migrations and before the application serves
traffic, so that a clean database becomes the MVP 1 railway without a manual step (EP-04; EP-01
seed-data mechanism; architecture.md §2 modules, §10 seed data).

#### Scenario: Start against an empty database
- **WHEN** the application starts against a database with the `network` schema migrated and no rows
- **THEN** two lines, nine stations with their platforms and 52 services with their stops are stored, and the public network and timetable endpoints return them

#### Scenario: Failed start leaves nothing behind
- **WHEN** either seed file is invalid
- **THEN** the application stops before serving traffic and no row from that run is committed

### Requirement: Seeding inserts what is missing and changes nothing that exists
Seeding SHALL be idempotent by natural key: a station by its code, a platform by its station and
number, a line by its code, and a service by its line, direction and name. A record already present
SHALL be left unchanged, a missing station, platform, line or service SHALL be inserted, and a
service SHALL be inserted together with all of its stops or not at all (EP-04; `add-identity-access`
`seed-data`; `IModuleSeeder`).

#### Scenario: Second start
- **WHEN** the application starts again against an already seeded database
- **THEN** the row counts are unchanged, no existing row is modified, and the seeder logs that it inserted nothing

#### Scenario: A row already exists with a different identifier
- **WHEN** the database already holds a station with the file's station code but a different ID
- **THEN** the station is left as it is, timetable references resolve to the stored station, and no duplicate is created

#### Scenario: Seed file edited
- **WHEN** a seeded service's name or times are changed in the file and the application restarts
- **THEN** the stored service and its stops are unchanged

### Requirement: The timetable refers only to the seeded network
Every service SHALL name a line in `network.json`, and every stop SHALL name a station and a platform
in `network.json`. A stop's platform SHALL belong to that stop's station. A service's stops SHALL be
that line's stations in the direction's order, with sequences starting at 1 and with no gaps
(EP-04; data-model.md `network`).

#### Scenario: Unknown station reference
- **WHEN** a service stop names a station ID that is not in `network.json`
- **THEN** start-up stops with an error naming the file, the service record and the stop

#### Scenario: Platform of another station
- **WHEN** a stop names a platform that exists but belongs to a different station
- **THEN** start-up stops with an error naming the file and the record

### Requirement: Seeded service stop shape
A seeded service SHALL have one stop per station of its line in the direction's order and a non-empty
guard staff ID. Its first stop SHALL have a departure and no arrival, its last stop an arrival and no
departure, and every other calling stop both times and a platform of its station (EP-04; `timetable`
spec; data-model.md).

#### Scenario: A service read back
- **WHEN** a seeded service is read with its stops
- **THEN** the stops are in order, each calling stop carries a platform of its station, and the first stop has no arrival and the last no departure

#### Scenario: A stop without a platform
- **WHEN** a calling stop in the file names no platform
- **THEN** start-up stops with an error naming the file and the record

### Requirement: Seeded times stay within one day
Times SHALL be local times of day that never decrease along a service's stops, so no seeded service
crosses midnight (EP-04; `timetable` spec; data-model.md).

#### Scenario: Times that go backwards
- **WHEN** a stop's arrival is earlier than the previous stop's departure
- **THEN** start-up stops, because that service would cross midnight

### Requirement: The seeded network is the MVP 1 network
The dataset in the repository SHALL describe two lines of five stations each, sharing exactly one
interchange station (nine distinct stations), running in both directions, with one or more numbered
platforms per station and more platforms at the interchange. Every seeded stop SHALL call at its
station, because skipped stops are deferred (EP-04; PAX-07; mvp-1-scope.md "The MVP 1 network and
timetable").

#### Scenario: Stations endpoint
- **WHEN** a client reads `GET /api/v1/network/stations`
- **THEN** nine stations are returned, exactly one has `isInterchange: true`, and it lists both lines

#### Scenario: Lines endpoint
- **WHEN** a client reads `GET /api/v1/network/lines`
- **THEN** two lines are returned, each with its five stations in outbound order

### Requirement: A journey is always available somewhere on the network
The seeded timetable SHALL ensure that, at every local time of day, at least one seeded service is
between its first departure and its last arrival, or has its first departure within the next 60
minutes, so that a board is never empty whenever the application starts and the hourly generation run
always has services to turn into journeys (EP-04 "done when: today's generated journeys are
queryable"; PAX-02, PAX-07; NC-01) [A].

#### Scenario: Start in the quiet hours
- **WHEN** the application starts at 03:00 railway time
- **THEN** today's journeys exist and at least one of them departs within 60 minutes

### Requirement: Invalid seed data stops start-up and names the fault
A missing, unreadable, malformed or rule-breaking seed file SHALL stop start-up with an
`InvalidOperationException` that names the file and, where a record is at fault, its 1-based position
in the file. Unexpected JSON properties, empty station/line/service arrays, duplicate identifiers,
duplicate natural keys and unknown references SHALL all be errors rather than silently skipped
records (`add-identity-access` `seed-data`; `DatabaseSeeder`; EP-04).

#### Scenario: Duplicate station code
- **WHEN** two stations in `network.json` use the same code
- **THEN** start-up stops with an error naming the file and the second record

#### Scenario: Missing file
- **WHEN** `seed/network/timetable.json` is not next to the binaries
- **THEN** start-up stops with an error naming the expected path, instead of serving an empty timetable

### Requirement: Seeding behaves the same in every environment
The seeder SHALL run in Development, E2E and Production with no enable switch: the only setting is
the existing `Seed:Path` root, defaulting to `seed` next to the binaries. The checked-in data SHALL be
described as demo data, not as a production timetable (EP-04; owner decision 2026-10-19; architecture.md
§10).

#### Scenario: Production-like start
- **WHEN** the application starts with `ASPNETCORE_ENVIRONMENT` set to `Production` against an empty database
- **THEN** the network and timetable are seeded exactly as in Development

### Requirement: Journeys are generated, not seeded
The seed files SHALL NOT contain journeys. After seeding, the existing start-up generation run SHALL
create one journey for today and one for tomorrow for every seeded service (EP-04; `journey-generation`).

#### Scenario: Journeys after start-up
- **WHEN** the application has finished starting
- **THEN** every seeded service has a journey dated today and one dated tomorrow in railway time, and a second start creates none

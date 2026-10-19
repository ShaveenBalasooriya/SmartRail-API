# Tasks

## 1. Network model and seed

- [ ] 1.1 Add `Line`, `Station`, `Platform`, `LineStation` domain types with the `network` migration; verify the migration creates the four tables with the unique constraints in the spec
- [ ] 1.2 Add the network seeder with full validation (duplicate codes, duplicate platform numbers, station without platform, station twice on a line); verify a unit test per rule and an integration test that a valid test seed loads idempotently

## 2. Timetable model and seed

- [ ] 2.1 Add `Service` and `ServiceStop` with their migration; verify tables and constraints
- [ ] 2.2 Add timetable validation (every line station present in direction order, first/last calling, ≥ 2 calling, non-calling has no time or platform, strictly increasing times, no midnight crossing, platform belongs to station, guard is a seeded `guard` via `IStaffDirectory`); verify one unit test per rule including each spec scenario

## 3. Railway clock and journey generation

- [ ] 3.1 Add `RailwayClock` (configured time zone over `TimeProvider`); verify unit tests for today/tomorrow at the local midnight boundary and 07:30 local → 02:00Z for UTC+05:30
- [ ] 3.2 Add the `journeys` migration, the generator with `ON CONFLICT DO NOTHING`, and the post-commit `JourneyGenerated` publication; verify integration tests: fresh start creates today + tomorrow per service, restart creates no duplicates and no second announcement
- [ ] 3.3 Add the midnight-triggered background run; verify an integration test that advances a fake `TimeProvider` past local midnight and sees the new tomorrow generated

## 4. Contracts and public endpoints

- [ ] 4.1 Implement `INetworkCatalog` and `ITimetable` from a post-seed cache; verify unit tests that interchange is derived and inbound order is reversed
- [ ] 4.2 Add `GET /network/lines`, `/network/stations`, `/network/stations/{id}` and `/timetable/journeys?date=`; verify integration tests: both lines in outbound order, interchange flagged with both lines, unknown station → 404, other date → 400, anonymous access works
- [ ] 4.3 Add a test seed (two 5-station lines, one interchange, a few services each way, seeded guards) under `tests/` and document the production seed format in `seed/README.md`; verify the integration suite runs on it

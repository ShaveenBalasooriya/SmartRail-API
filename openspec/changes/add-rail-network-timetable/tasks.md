# Tasks

## 1. Network model and seed

- [ ] 1.1 Add `Line`, `Station`, `Platform`, `LineStation` domain types with the `network` migration; verify the migration creates the four tables with the unique constraints in the spec
- [ ] 1.2 Add the network seeder with the spec's validation rules, stopping at the first problem; verify start-up loads the test seed and a second start creates no duplicates

## 2. Timetable model and seed

- [ ] 2.1 Add `Service` and `ServiceStop` with their migration; verify a clean build and start-up
- [ ] 2.2 Add the timetable validation rules from the spec (guard checked through `IStaffDirectory`); verify unit tests for the time-ordering and midnight rules, and start-up with the test seed

## 3. Railway clock and journey generation

- [ ] 3.1 Add `RailwayClock` (configured time zone over `TimeProvider`); verify unit tests for today/tomorrow at the local midnight boundary and 07:30 local → 02:00Z for UTC+05:30
- [ ] 3.2 Add the `journeys` migration, the generator with `ON CONFLICT DO NOTHING`, and the in-transaction `JourneyGenerated` publication; verify a start creates today + tomorrow per service and a restart creates no duplicates
- [ ] 3.3 Add the hourly re-run; verify a clean build

## 4. Contracts and public endpoints

- [ ] 4.1 Implement `INetworkCatalog` and `ITimetable` from a post-seed cache; verify a unit test that interchange is derived and inbound order is reversed
- [ ] 4.2 Add `GET /network/lines`, `/network/stations`, `/network/stations/{id}` and `/timetable/journeys?date=`; verify by calling them anonymously on the running app
- [ ] 4.3 Add a test seed (two 5-station lines, one interchange, a few services each way, seeded guards) and document the seed format in `seed/README.md`; verify the app starts on it

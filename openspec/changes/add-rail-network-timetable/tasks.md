# Tasks

## 1. Network and timetable model

- [x] 1.1 Add `Line`, `Station`, `Platform`, `LineStation` to the RailNetwork domain with the first `network` migration; verify the build and that the migration creates the four tables with unique line code, station code and (station, platform number)
- [x] 1.2 Add `Service` and `ServiceStop` (guard staff ID as a plain column) with their migration; verify the build and start-up

## 2. Railway clock and journey generation

- [x] 2.1 Add `RailwayClock` (configured time zone over `TimeProvider`); verify the build and that 07:30 local on a UTC+05:30 zone gives 02:00Z
- [x] 2.2 Add the `journeys` migration, the generator with `ON CONFLICT DO NOTHING` and the `JourneyGenerated` notification; verify the app starts with empty tables and a restart does not fail
- [x] 2.3 Add the hourly re-run; verify the build and start-up

## 3. Contracts and public endpoints

- [x] 3.1 Implement `INetworkCatalog` and `ITimetable` in `SmartRail.RailNetwork.Contracts`; verify the build
- [x] 3.2 Add `GET /network/lines`, `/network/stations`, `/network/stations/{id}` and `/timetable/journeys?date=`; verify by calling them on the running app (empty lists, 404 for an unknown station, 400 for a date outside today and tomorrow)

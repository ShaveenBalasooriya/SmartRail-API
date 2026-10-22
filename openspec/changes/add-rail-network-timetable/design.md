# Design: Rail network, timetable and daily journeys

## Context

Tables are fixed by `../docs/data-model.md` (`network` schema, 7 tables). Owner decisions: timetable
is seed data, every service runs daily, no midnight crossing, one guard per service. Identity's
seeder runs before this module's, so guard references can be checked.

## Goals / Non-Goals

**Goals:** a validated, immutable-at-runtime network and timetable; journeys that exist before anyone
needs them; a clean baseline handed to Live Operations.

**Non-Goals:** editing, calendars, per-journey overrides.

## Decisions

### D1. Seed file format
`seed/network/network.json` (lines with ordered station codes; stations with code, name, platforms)
and `seed/network/timetable.json` (services: code, name, line code, direction, guard username, stops:
station code, calling, arrive, depart, platform number). Natural keys: line code, station code,
`(station, platform number)`, service code. Validation runs before any insert and stops at the first
problem, naming the file and record. The seed is small and owner-written, so fixing one problem at a
time is fine.

### D2. Railway time zone
`Railway:TimeZone` configuration, default `Asia/Colombo`, resolved with `TimeZoneInfo` (IANA ids
work on Linux and on Windows with .NET's ICU). All conversions go through one `RailwayClock`
service built on `TimeProvider`, giving "today", "tomorrow" and local-time-to-UTC for a date.

### D3. Generation job
Generation runs at start-up (after seeding), and a `BackgroundService` with a `PeriodicTimer` re-runs
it every hour. Because generation is idempotent, the hourly run picks up the new tomorrow shortly
after local midnight without any midnight scheduling logic. Insert uses `INSERT ... ON CONFLICT (service_id, service_date) DO
NOTHING RETURNING id`; only returned rows are announced via MediatR `JourneyGenerated`, published inside the
request unit of work from `add-audit-record` (D1 there), so Live Operations creates the journey's live
state in the same transaction. If that handler fails, the journey insert rolls back too and the next
hourly run retries; a journey can never exist without live state. (Changes to live state itself are
announced after commit; see `add-live-running-state` D5.)

### D4. Baseline in the announcement
`JourneyGenerated` carries every stop: sequence, station ID, calling flag, scheduled arrival and
departure as UTC instants, platform ID, plus the service's line, direction and guard staff ID. Live
Operations therefore never needs the timetable at runtime for an existing journey.

### D5. Contracts and endpoints
`INetworkCatalog` (lines, stations, platforms, line order, interchange) and `ITimetable` (service and
stops) in `SmartRail.RailNetwork.Contracts`, served from an in-memory cache built after seeding —
the data never changes at runtime. Endpoints: `GET /api/v1/network/lines`,
`GET /api/v1/network/stations`, `GET /api/v1/network/stations/{id}`,
`GET /api/v1/timetable/journeys?date=`; all `.AllowAnonymousPublic()`.

## Risks / Trade-offs

- [Journeys missed if the app is down over midnight] → Start-up generation covers today and tomorrow,
  so a restart repairs any gap.
- [Late announcement handler failure leaves a journey without live state] → Not possible: the
  announcement is handled in the same transaction as the insert (D3).

## Open Questions

- Station names and the timetable content are the owner's to supply; the tasks ship a placeholder
  seed for tests only.

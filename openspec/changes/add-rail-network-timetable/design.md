# Design: Rail network, timetable and daily journeys

## Context

Tables are fixed by `../docs/data-model.md` (`network` schema, 7 tables). Decisions: every service
runs daily, no midnight crossing, one guard per service. There is no seeding in this change, so the
tables are empty until data is loaded by a later change; tests insert rows directly.

## Decisions

### D1. Railway time zone
`Railway:TimeZone` configuration, default `Asia/Colombo`, resolved with `TimeZoneInfo`. All
conversions go through one `RailwayClock` service built on `TimeProvider`, giving "today",
"tomorrow" and local-time-to-UTC for a date.

### D2. Generation job
Generation runs at start-up and a `BackgroundService` with a `PeriodicTimer` re-runs it every hour.
It is idempotent, so the hourly run picks up the new tomorrow after local midnight without any
midnight scheduling logic. Insert uses `INSERT ... ON CONFLICT (service_id, service_date) DO NOTHING
RETURNING id`; only returned rows are announced through MediatR as `JourneyGenerated`. With no
services in the database the run does nothing.

### D3. Baseline in the announcement
`JourneyGenerated` carries every stop: sequence, station ID, calling flag, scheduled arrival and
departure as UTC instants, platform ID, plus the service's line, direction and guard staff ID, so
Live Operations never needs the timetable at runtime for an existing journey. Making the insert and
the live-state creation one transaction is left to `add-live-running-state`.

> **Clarified 2026-10-21 (owner decision; `add-event-bus`):** `JourneyGenerated` is no longer published
> directly through MediatR. It goes through the event bus: an outbox row in the `network` schema saved in
> the same transaction as the inserted journeys, delivered at least once to Live Operations'
> `live-operations.journey-generated` consumer. The code change is a task in `add-event-bus` (2.3); this
> change's history is not rewritten.

### D4. Contracts and endpoints
> **Superseded by refactor-module-architecture:** `SmartRail.RailNetwork.Contracts` now holds only the
> interfaces and DTOs; `INetworkCatalog` and `ITimetable` are implemented in
> `SmartRail.RailNetwork.Application`, and the endpoints live in the Api project.

`INetworkCatalog` (lines, stations, platforms, line order, interchange) and `ITimetable` (service
and stops) in `SmartRail.RailNetwork.Contracts`, read from the database. Endpoints:
`GET /api/v1/network/lines`, `GET /api/v1/network/stations`, `GET /api/v1/network/stations/{id}`,
`GET /api/v1/timetable/journeys?date=`. Authorisation is not applied yet; it arrives with the
identity change.

## Risks / Trade-offs

- [Journeys missed if the app is down over midnight] → Start-up generation covers today and tomorrow,
  so a restart repairs any gap.
- [Nothing validates timetable data] → Accepted for now; validation comes with whatever loads it.

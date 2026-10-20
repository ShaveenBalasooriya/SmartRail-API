# Proposal: Rail network, timetable and daily journeys

## Why

Every live feature reads from the network and the timetable: departures, progress, platform changes
and the catering feed all describe operational journeys on a line of stations. This change builds the
model, the daily journey generation and the read endpoints. How network and timetable data gets into
the database is not decided here; there is no seeding or loader in this change, and tests insert
their own rows.

Delivers **EP-04** (structure only). Supports PAX-02, PAX-03, PAX-07, GRD-01, CAT-02.

## What Changes

- The `network` schema: lines, stations, platforms, each line's station order, services with their
  stops, and dated journeys.
- Daily journey generation for today and tomorrow in the railway's time zone, idempotent, announcing
  each new journey so Live Operations can create its live state.
- Public read endpoints for lines, stations and the scheduled journeys of a date; in-process query
  contracts other modules use for the same data.

## Capabilities

### New Capabilities

- `rail-network`: lines, stations, platforms, station order and the interchange.
- `timetable`: services, their stopping patterns, times, platforms and guard.
- `journey-generation`: turning services into dated operational journeys every day.

### Modified Capabilities

None.

## Out of Scope

- Seeding or any way of loading network or timetable data, and validating it (decided in a later
  change).
- Checking that a service's guard is a Guard; the guard is stored as a plain staff ID.
- The Scheduler role, endpoints that create or edit network data, the 24-hour boundary (EP-05).
- Running-day calendars (every service runs every day), services that cross midnight, driver
  assignment, per-journey crew changes, station accessibility data.
- Live state of any kind: `add-live-running-state`.

## Impact

- New `network` schema (`lines`, `stations`, `platforms`, `line_stations`, `services`,
  `service_stops`, `journeys`).
- `SmartRail.RailNetwork.Contracts`: `INetworkCatalog`, `ITimetable`, `JourneyGenerated`
  notification.
- Public endpoints under `/api/v1/network` and `/api/v1/timetable`.

# Proposal: Rail network, timetable and daily journeys

## Why

Every live feature reads from the network and the timetable: departures, progress, platform changes
and the catering feed all describe operational journeys on a line of stations. In the lean MVP 1 the
network and timetable are seed data and there is no Scheduler, so the system must load them, check
them and generate each day's operational journeys on its own (mvp-1-scope.md, Owner decisions).

Delivers **EP-04**. Supports PAX-02, PAX-03, PAX-07 (calling stops, stations, interchange), GRD-01
(the guard on each journey), CAT-02 (stations and platforms).

## What Changes

- Seeded lines, stations, numbered platforms and each line's station order; the interchange is the
  station on both lines.
- Seeded timetable: services per line and direction, with a stop row for every station on the line
  (calling or not), scheduled times, a platform per calling stop and the assigned guard.
- Validation of the seed data on start-up.
- Daily journey generation for today and tomorrow in the railway's time zone, idempotent, announcing
  each new journey so Live Operations can create its live state.
- Public read endpoints for lines, stations (with platforms and lines) and the scheduled journeys of
  a date; an in-process query contract other modules use for the same data.

## Capabilities

### New Capabilities

- `rail-network`: lines, stations, platforms, station order and the interchange.
- `timetable`: the seeded services, their stopping patterns, times, platforms and guards, and the
  rules they must satisfy.
- `journey-generation`: turning the timetable into dated operational journeys every day.

### Modified Capabilities

None.

## Out of Scope

- The Scheduler role, any endpoint that creates or edits lines, stations, platforms or services, the
  24-hour boundary (deferred, EP-05).
- Running-day calendars: every service runs every day (owner decision).
- Services that run past midnight (rejected by validation).
- Driver assignment and per-journey crew changes.
- Station accessibility data (EP-04 Out).
- Live state of any kind — `add-live-running-state`.

## Impact

- New `network` schema (`lines`, `stations`, `platforms`, `line_stations`, `services`,
  `service_stops`, `journeys`), seed files under `seed/network/`.
- `SmartRail.RailNetwork.Contracts`: `INetworkCatalog`, `ITimetable`, `JourneyGenerated`
  notification.
- Uses Identity's `IStaffDirectory` contract to check that each service's guard is a seeded Guard.
- Public endpoints under `/api/v1/network` and `/api/v1/timetable`.

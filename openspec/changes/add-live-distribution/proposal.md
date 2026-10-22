# Proposal: Live distribution to passengers, the display and the Guard

## Why

The point of MVP 1 is that one change reaches every channel (M-01, EP-09). The passenger app, the
station platform display and the Guard's app need a public read API over the single live state and a
live stream that delivers each change, keeps them honest about freshness, and lets them catch up after
a dropped connection (M-02, M-09, M-10, M-11, M-12, PAX-02 to PAX-16).

Delivers **EP-09** and the backend of **EP-10** and **EP-11**. Depends on `add-live-running-state`,
`add-rail-network-timetable`, `add-identity-access` and `add-event-bus` (the hub consumes
`JourneyStateChanged`).

## What Changes

- Public read endpoints: departures from station A to station B, onward departures from a station,
  journey detail with calling stops and progress, and a station display feed for one platform.
- Every item carries scheduled and expected times, minutes early or late, platform, status,
  platform-change time, provenance, correction note, and last-updated time.
- Template guidance text and the next service to the same destination for disrupted journeys.
- Server-Sent Events streams: a public stream (all journeys, or filtered by station or journey) and a
  Guard stream scoped to the guard's own journeys, with event IDs, resume from `Last-Event-ID`,
  heartbeats and a bounded catch-up window.

## Capabilities

### New Capabilities

- `passenger-departures`: public departures, connections and journey detail.
- `disruption-guidance`: plain-language "what to do next" with the next viable service.
- `station-display-feed`: the large-display departure feed for a station and platform.
- `live-event-streams`: the SSE streams, resume and catch-up.

### Modified Capabilities

None.

## Out of Scope

- The passenger mobile app, the display page and the Guard app themselves (clients).
- Passenger accounts, the journey planner, saved journeys, watch and local alerts (deferred); clients
  keep their own last-known cache and stale marker.
- Server push, SMS, notification preferences (S-01).
- Recovery alternatives beyond the next service to the same destination (S-05).
- Audio output, interactive terminal, multi-language text.
- Station accessibility data.

## Impact

- `Distribution` module endpoints under `/api/v1/public` and `/api/v1/guard/stream`; no tables.
- Uses `ILiveJourneys` and `INetworkCatalog`; public endpoints use the `public` rate-limit policy.

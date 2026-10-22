# Design: Live distribution

## Context

Distribution keeps no tables (architecture §3, data-model.md). It composes `ILiveJourneys` and
`INetworkCatalog` and consumes the `JourneyStateChanged` message from the event bus (`add-event-bus`;
owner decision 2026-10-21, no message broker in MVP 1; architecture §3 "Event delivery"). SSE is the only live transport in MVP 1 (architecture §6).

## Goals / Non-Goals

**Goals:** identical data on app and display by construction; resumable streams; no per-client
state outside memory.

**Non-Goals:** persistence of subscriptions, push notifications, a second transport.

## Decisions

### D1. One view model
A single `PublicJourneyView` builder produces the item returned by departures, detail, display feed
and stream payloads, so the "same data" requirement holds by construction. Guidance text is added by
`GuidanceBuilder` (fixed templates in code, not in the database).

### D2. Endpoints (all `.AllowAnonymousPublic()` unless noted)
- `GET /api/v1/public/departures?from=&to=` · `GET /api/v1/public/stations/{id}/departures?windowMinutes=`
- `GET /api/v1/public/journeys/{id}`
- `GET /api/v1/public/display/{stationId}/platforms/{number}?count=`
- `GET /api/v1/public/stream?station=&journey=` (SSE)
- `GET /api/v1/guard/stream` (SSE, Guard role)

### D3. Stream implementation
A singleton `StreamHub` holds one unbounded `Channel<StreamEnvelope>` per connection, so a write to one
connection never waits on another. The hub is an `IEventConsumer<JourneyStateChanged>` named
`distribution.stream-hub` [A]: it loads the event snapshot via `ILiveJourneys` and offers it to each
connection whose filter matches. Because the bus delivers at least once, the consumer ignores an event ID
it handled recently (a bounded in-memory set) [A]. The endpoint writes `id:`, `event:` and `data:` frames
with `TypedResults.ServerSentEvents` (.NET 10) and flushes after each. A heartbeat comment is written every
15 seconds. There is no slow-consumer cut-off: MVP 1 has a handful of clients and small events, so a
stalled client's buffer stays small until its connection drops.

### D4. Resume without gaps or duplicates
On connect with `Last-Event-ID = N`: register the connection in the hub in *buffering* mode first, then
read events `> N` from `ILiveJourneys`, send them, then drain the buffer skipping any event whose ID was
already sent in the catch-up [A], then go live. Registering before reading closes the gap; skipping by
event ID removes the overlap. The bus promises order only per journey (`add-event-bus` D6), so events of
different journeys can reach the hub in a different order than their stream positions; skipping by ID
rather than by position keeps such an event from being dropped. The residual risk `add-live-running-state`
D3 accepted, an event whose position commits late while a client resumes, stays accepted.

### D5. Retained window
`ILiveJourneys.OldestRetainedPosition` decides `reset`. MVP 1 deletes nothing, so `reset` happens only
for unknown or future IDs.

### D6. Next service lookup
`GuidanceBuilder` asks `ILiveJourneys` for today's journeys from the same origin stop to the same
final calling stop with a later scheduled departure and status neither `cancelled` nor `terminated`, taking the earliest.

### D7. Cache and cookies
Public responses set `Cache-Control: no-store`; no cookies are issued; the rate-limit partition key is
held in memory only (`add-identity-access` D6).

## Risks / Trade-offs

- [In-memory hub is lost on restart, all streams drop] → Clients reconnect with `Last-Event-ID`
  and receive catch-up; this is the design.
- [A stalled client's buffer grows until it disconnects] → Small at MVP 1 volume; add a bounded
  buffer and cut-off if the number of clients grows.
- [A single instance only] → MVP 1 runs one instance; multiple instances would need a shared
  backplane, which is the owner's expected trigger for adopting NATS JetStream (noted for later).
- [Window read of departures is computed on request] → Dataset is one day of nine stations; no
  caching needed.

## Open Questions

- **Client-side stale threshold [A].** EP-09 (`../docs/scoping/mvp-1-scope.md`) asks for a stale marker
  "after a defined threshold", but no spec defines that threshold for the passenger app, the station
  display or the Guard app. Only the controller view defines one (`Operations:StaleAfterMinutes`,
  default 10). This change only supplies `generatedAt`, the last-updated time and a recommended refresh
  interval; the threshold itself needs the owner's decision and is deliberately not invented here.

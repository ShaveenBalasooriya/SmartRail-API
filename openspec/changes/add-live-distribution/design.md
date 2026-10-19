# Design: Live distribution

## Context

Distribution keeps no tables (architecture §3, data-model.md). It composes `ILiveJourneys` and
`INetworkCatalog` and subscribes to `JourneyStateChanged`. SSE is the only live transport in MVP 1
(architecture §6).

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
A singleton `StreamHub` holds one bounded `Channel<StreamEnvelope>` (capacity 256, `DropWrite` then
mark overflow) per connection. A MediatR `JourneyStateChanged` handler loads the event snapshot via
`ILiveJourneys` and offers it to each connection whose filter matches. The endpoint writes `id:`,
`event:` and `data:` frames with `TypedResults.ServerSentEvents` (.NET 10) and flushes after each.
Overflow closes the connection.

### D4. Resume without gaps or duplicates
On connect with `Last-Event-ID = N`: register the connection in the hub in *buffering* mode first,
then read events `> N` from `ILiveJourneys`, send them, then drain the buffer skipping any event with
position ≤ the last position sent, then go live. Registering before reading closes the gap; skipping
by position removes the overlap. The safety window from `add-live-running-state` D3 is applied to the
catch-up read.

### D5. Retained window
`ILiveJourneys.OldestRetainedPosition` decides `reset`. MVP 1 deletes nothing, so `reset` happens only
for unknown or future IDs.

### D6. Next service lookup
`GuidanceBuilder` asks `ILiveJourneys` for today's journeys from the same origin stop to the same
final calling stop with a later scheduled departure and status not `cancelled`, taking the earliest.

### D7. Cache and cookies
Public responses set `Cache-Control: no-store`; no cookies are issued; the rate-limit partition key is
held in memory only (`add-identity-access` D6).

## Risks / Trade-offs

- [In-memory hub is lost on restart, all streams drop] → Clients reconnect with `Last-Event-ID`
  and receive catch-up; this is the design.
- [A single instance only] → MVP 1 runs one instance; multiple instances would need a shared
  backplane (noted for later).
- [Window read of departures is computed on request] → Dataset is one day of nine stations; no
  caching needed.

## Open Questions

None.

# Design: Catering provider integration

## Context

Tables follow `../docs/data-model.md` (`catering.providers`, `webhook_subscriptions`,
`webhook_deliveries`, unique `(subscription_id, event_id)`). Identity authenticates the key and asks
`ICateringProviderDirectory` whether the provider is active. The provider reads stations and journeys
through `INetworkCatalog` and `ILiveJourneys`; there is no read model.

## Goals / Non-Goals

**Goals:** reliable, ordered delivery with an outbox; a simulated provider that is a real separate
consumer; a deliberately narrow API.

**Non-Goals:** signing, orders, snapshots, multiple providers.

## Decisions

### D1. Outbox in the event transaction
A MediatR notification handler runs **inside** the unit of work for the five pushed event types and
inserts one `webhook_deliveries` row per active subscription (`status = pending`, `next_attempt_at =
now`), copying the event payload. The row therefore commits atomically with the event. The unique
`(subscription_id, event_id)` index plus `ON CONFLICT DO NOTHING` gives one delivery per event.
*Alternative:* send from the post-commit notification — rejected: a crash between commit and send
loses the change.

### D2. Dispatcher
A `BackgroundService` polls every second for due deliveries using
`SELECT ... FOR UPDATE SKIP LOCKED`. For ordering it selects, per journey, only the lowest-sequence
delivery that is not `delivered` or `abandoned`, and sends it if due. An `HttpClient` with a 10 s
timeout posts the payload. 2xx → `delivered`; otherwise increment `attempt_count`, set
`next_attempt_at` from the backoff table, `abandoned` after the fifth failure.

### D3. Endpoints (all `.RequireProvider()`)
- `GET /api/v1/provider/stations`, `GET /api/v1/provider/stations/{id}`
- `GET /api/v1/provider/stations/{id}/journeys?date=`
- `PUT /api/v1/provider/webhook` (register or replace), `GET /api/v1/provider/webhook`,
  `DELETE /api/v1/provider/webhook` (deactivate)

### D4. Callback URL safety
Validated at registration and again at send time: resolved addresses must not be loopback,
link-local or private unless `Catering:AllowPrivateCallbacks` is true; redirects are not followed.
Query strings are removed from logged URLs.

### D5. Provider payload
Versioned JSON `{ "version": 1, "eventId", "sequenceNumber", "type", "journeyId", "stopSequence",
"stationId", "values": {...}, "publishedAt", "provenance", "correctsEventId" }`. Types are the
four pushed `event_type` values in snake_case.

### D6. Simulated provider
Separate ASP.NET Core minimal app `SmartRail.SimulatedProvider` with SQLite for its own state, a
`POST /webhook` endpoint, and a single-page kitchen screen updated over its own SSE. Configured with
SmartRail's base URL and its API key. Applies events by event-ID de-duplication and per-journey
last-applied sequence; keeps the applied events per journey so a correction recomputes state the same
way SmartRail does. Failure switch: `POST /admin/fail` toggles 503. Added to `compose.yaml`.

### D7. Metrics
OpenTelemetry counters `smartrail.webhook.attempts`, `smartrail.webhook.failures`,
`smartrail.webhook.abandoned`, and a histogram of publish-to-delivery latency (for EP-17).

## Risks / Trade-offs

- [Skipping unpushed sequence numbers looks like loss to a naive consumer] → Documented in the
  provider API description and the simulated provider relies only on "higher than last applied".
- [A journey's queue blocked by one bad event for ~43 minutes of backoff] → Bounded by the retry
  limit; abandon releases the queue.
- [Outbox table grows] → Retention deferred (EP-14 Out); negligible at MVP 1 volume.

## Open Questions

None.

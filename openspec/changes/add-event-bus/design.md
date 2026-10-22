# Design: Event bus

## Context

Owner decision 2026-10-21 (`../docs/architecture.md` §3, "Event delivery: a generalised event bus on
Postgres now, NATS JetStream later"): every cross-module event travels one mechanism, there is no
message broker in MVP 1 (time constraints), and NATS JetStream is the broker expected later. Today
`JourneyGenerated` is published in-process right after save (`GenerateJourneysCommandHandler`),
`JourneyStateChanged` after commit (`ApplyJourneyFactCommand`) and `AccessDenied` in-process from
`AccessDeniedAuthorizationResultHandler`. `operations.journey_events` stays the query and catch-up store
behind `ILiveJourneys`; the bus does not replace it. Everything below is the orchestrator's and this
change's proposal unless marked as an owner decision (D11): all of it is [A].

## Goals / Non-Goals

**Goals:** one publishing path and one delivery path for every cross-module event; a published event is
never lost to a crash or a rollback-after-publish; a later NATS JetStream transport changes one
implementation, not publishers or consumers.

**Non-Goals:** a broker, global ordering, re-driving failed deliveries, outbox retention, multiple API
instances, and moving staff state-change audit onto the bus (the owner's planned later step, D11).

## Decisions

### D1. One publisher, an outbox in the publishing module's own schema [A]
A module publishes by calling `IIntegrationEventPublisher.Publish(event, eventId, orderingKey?)`
(`SmartRail.SharedKernel.Application`). The implementation adds an `outbox_messages` row through the
publishing module's own `DbContext`, so it is saved by the same `SaveChangesAsync` and the same
transaction as the change (architecture §4: no cross-schema write). A rollback therefore publishes
nothing. Cross-module events are no longer published with MediatR `IPublisher`; MediatR stays for
commands, queries and in-module notifications.

### D2. Message shape [A]
`outbox_messages`: `id` (the event ID; for `JourneyStateChanged` the journey event's ID), `position`
(identity column, the dispatch order), `event_type`, `ordering_key` (nullable), `payload` (JSON of the
event record), `occurred_at`, `published_at` (null until the transport accepted it). The three contract
records stay plain records (they stop being MediatR `INotification`). Payloads follow the existing rule:
no passenger data, token, API key or client IP.

### D3. Dispatcher and transport [A]
A thin `BackgroundService` in `SmartRail.SharedKernel.Infrastructure` (D8) reads every module's
unpublished outbox rows in `position` order and hands each to `IEventTransport.Publish(message,
consumers)`, then sets `published_at`. It finds the modules' outboxes through one small
`IOutboxSource` each publishing module registers, so it needs no module knowledge. MVP 1's
`IEventTransport` is in-process on PostgreSQL: `Publish` creates one pending `outbox_deliveries` row per
registered consumer of that event type (`ON CONFLICT DO NOTHING`, so a crash between `Publish` and
`published_at` is harmless) and the same implementation delivers due rows (D5). A later NATS JetStream
transport would publish with the event ID as the `Nats-Msg-Id` header and bind the registered consumers as
durable consumers, so per-consumer state, redelivery and replay move into JetStream. Publishers and
consumers see only `IIntegrationEventPublisher` and `IEventConsumer<T>`, so that swap touches neither. The
transport lives in Infrastructure and no module Application or Contracts project references it.

### D4. Consumers: `IEventConsumer<TEvent>`, not `INotificationHandler<T>` [A]
`IEventConsumer<TEvent>` has a stable `Name` and a `Handle(TEvent, CancellationToken)`; a module
registers it with one DI extension. It is a small change, because the existing handlers
(`JourneyGeneratedHandler`, the audit denial handler) keep their bodies and only change interface and
gain a name. Chosen over keeping `INotificationHandler<T>` because (1) per-consumer delivery state needs a
stable explicit name, which is also the durable-consumer name later, and a handler's type name would
silently re-deliver everything if renamed; (2) the dispatcher invokes exactly the registered consumers,
so no event can run through two mechanisms; (3) MediatR's publisher would otherwise have to be bypassed to
call one handler at a time anyway. A consumer SHALL be idempotent by event ID: delivery is at-least-once.
Consumers write only to their own module's schema.

### D5. Delivery state, retry and `failed` [A]
`outbox_deliveries` (publisher's schema): `message_id`, `consumer`, `status` (`pending`, `delivered`,
`failed`), `attempt_count`, `next_attempt_at`, `last_attempt_at`, `delivered_at`, unique
`(message_id, consumer)`. Delivery runs the consumer in a new DI scope. Success marks `delivered`. An
exception counts an attempt and sets `next_attempt_at` from 2 s, 10 s, 1 min, 5 min; the fifth failure
marks `failed`. A consumer's state is its own rows, so one failing consumer never blocks or repeats
delivery to another. A restart leaves pending rows pending; the dispatcher delivers them after start-up.
The consumer's own work and the `delivered` mark are in different schemas and so not atomic: a crash
between them re-delivers, which D4's idempotency absorbs.

### D6. Ordering [A]
For one consumer and one `ordering_key`, deliveries run in `position` order, one at a time: a later
message waits while an earlier one for the same consumer and key is `pending`; a `failed` one releases
the queue. For journey events the key is the journey ID, and the per-journey sequence is assigned under
the journey's row lock in the same transaction as the outbox row, so `position` order within a key is the
sequence order Catering needs (CAT-07). Different keys, and messages with no key, carry no ordering
promise.

### D7. Wake-up and poll; the shared transaction [A]
After `SaveChangesAsync` the publisher signals the dispatcher in-process (a `DbContext` `SavedChanges`
hook sets a `Channel` signal) so latency stays low; the dispatcher also polls every second as the
fallback. Only committed rows are readable by the dispatcher, so nothing is delivered before commit. This
also covers the shared request transaction of `add-audit-record` D1: the `SaveChangesAsync` may happen
before the final commit, so the signal can find nothing yet, and the next poll finds the row once the
transaction commits. The outbox row commits or rolls back together with the change and the audit entry.

### D8. A shared infrastructure project [A]
Outbox mapping, the publisher, the dispatcher and the in-process transport need EF Core and Npgsql, which
neither `SmartRail.SharedKernel.Application` nor a module's Application may reference. They therefore live
in a new `SmartRail.SharedKernel.Infrastructure` project that module Infrastructure projects and the Host
reference. This makes the Shared Kernel four projects, and the architecture tests are extended.

### D9. The three events [A]
- `JourneyGenerated` (Rail Network): published from `GenerateJourneysCommandHandler` in the same
  transaction as the inserted journeys (`network.outbox_*`); no ordering key; consumer
  `live-operations.journey-generated` creates the live state. The start-up backfill of live state stays as
  built and is not removed.
- `JourneyStateChanged` (Live Operations): published by `ApplyJourneyFact` in the sink's transaction
  (`operations.outbox_*`); event ID = the journey event ID; ordering key = the journey ID; unchanged
  contents. Consumers load the event through `ILiveJourneys`: `catering.webhook-deliveries`
  (`add-catering-integration`) and `distribution.stream-hub` (`add-live-distribution`).
- `AccessDenied` (Identity): published from the authorization result handler through Identity's own
  `DbContext` with its own save (`identity.outbox_*`; the denied request has no business transaction); no
  ordering key; consumer `audit.access-denied` writes the entry in Audit's own transaction. A denial is no
  longer lost if the audit write fails at that moment, because the delivery is retried
  (`add-audit-record` D5).

### D10. Observability [A]
`System.Diagnostics.Metrics` counters `smartrail.events.delivery_failures` and
`smartrail.events.deliveries_failed` (the `failed` state), exported by the OpenTelemetry setup that
`add-critical-e2e-flows` adds, labelled only by `event_type` and `consumer`. Each failed attempt is logged
with the event ID, consumer name, attempt number and exception type, never the payload.

### D11. Audit of staff state changes: the shared transaction now, the bus later
Owner decision 2026-10-21: staff state changes keep the single exception, an audit entry committed with the
change through the shared request transaction (option X; `add-audit-record` D1), with the small coupling it
creates acknowledged and accepted for simplicity. The owner's plan is to move them onto this bus later
(option Y). To keep that possible without touching any caller, every staff state change SHALL call
`IAuditRecorder` (the existing `SmartRail.Audit.Contracts` interface) and nothing else: callers, for example
Live Operations, never touch the shared request transaction or an outbox for audit. Only the recorder's
implementation then changes, from "add the entry in the shared transaction" to "publish an audit message
through `IIntegrationEventPublisher`" read by an Audit consumer. Access denials already travel this bus
(D9), which is the first step on that path. No task here builds Y.

## Risks / Trade-offs

- [One dispatcher in one process: a second API instance would deliver twice] → MVP 1 runs one instance;
  more than one is the owner's expected trigger for the NATS transport.
- [A `failed` delivery is never retried again] → It is counted and logged; re-driving is out of scope. The
  consequence for Catering is a missing webhook, so the limit (about six minutes of outage) is the thing to
  revisit first.
- [A slow or hung consumer delays the sequential dispatcher] → Consumers only write to their own schema; no
  timeout is added at MVP 1 volume.
- [Outbox tables grow] → Retention deferred (EP-14 Out); negligible at MVP 1 volume.
- [Events across different journeys can be delivered in a different order than their stream positions] →
  Only per-key order is promised. The live streams' resume rule accounts for it
  (`add-live-distribution` D4); the residual out-of-order position risk `add-live-running-state` D3
  accepted stays accepted.

## Open Questions

- **A fourth Shared Kernel project (D8).** `SmartRail.SharedKernel.Infrastructure` is needed to share the
  outbox and dispatcher; the architecture documents said three projects. Confirm.
- **Failed deliveries.** Is "counted and logged, not re-driven" enough for MVP 1?

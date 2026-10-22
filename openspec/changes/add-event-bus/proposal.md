# Proposal: Event bus

## Why

Cross-module events reach their consumers in three different ways today: `JourneyGenerated`
(Rail Network) is published in-process right after save, `JourneyStateChanged` (Live Operations) is
published after the transaction commits, and `AccessDenied` (Identity) is published in-process from the
authorization handler. Each has its own timing and none survives a crash between the change and the
reaction. The owner decided on 2026-10-21 to use one mechanism for every cross-module event: a
generalised event bus on PostgreSQL now, with NATS JetStream as a transport swap later
(`../docs/architecture.md` §3, "Event delivery"). There is no message broker in MVP 1 (time
constraints; one API instance and a handful of consumers). Without this change the Catering consumer,
the live streams and the audit of denials would each need their own delivery code (EP-01 domain event
log, EP-09, EP-12, EP-03).

Delivers the event-delivery half of **EP-01** (domain event log, real-time channel). Stories served:
CAT-05 to CAT-09 (reliable ordered change delivery), AUD-04 (denials recorded), EP-09 (every channel
receives every change). Depends on the built `add-identity-access`, `add-rail-network-timetable` and
`add-live-running-state`.

**Build order.** `add-event-bus` comes next, before `add-audit-record`, `add-departure-logging`,
`add-controller-operations`, `add-catering-integration` and `add-live-distribution`, because each of
those publishes or consumes an integration event. `add-critical-e2e-flows` stays last.

## What Changes

- A single way to publish a cross-module integration event: an `IIntegrationEventPublisher` that writes
  an outbox row in the publishing module's own schema, in the same transaction as the change.
- A generic dispatcher that reads each module's unpublished outbox rows in order and hands every message
  to every registered consumer through an `IEventTransport`. MVP 1's transport is in-process on
  PostgreSQL; a NATS JetStream transport later is a swap of that one implementation.
- A small consumer interface, `IEventConsumer<TEvent>`, with a stable consumer name, idempotent by event
  ID, with its own delivery state, retry with backoff and a `failed` state.
- Per-ordering-key delivery order (the journey ID for journey events) and no global ordering promise.
- The three existing events move onto the bus (code changes to the built changes are tasks here; their
  history is not rewritten): `AccessDenied`, `JourneyStateChanged`, `JourneyGenerated`.
- Delivery-failure metrics labelled only by event type and consumer name.

## Capabilities

### New Capabilities

- `event-publishing`: how a module publishes an integration event atomically with its own change.
- `event-delivery`: how published events reach consumers: dispatch, ordering, idempotency, retry,
  failure visibility and the transport seam.

### Modified Capabilities

None. The built changes keep their specs; clarification notes point here and the consumers' own changes
(`add-audit-record`, `add-catering-integration`, `add-live-distribution`) state the new behaviour.

## Out of Scope

- The NATS JetStream transport itself, and any message broker (owner decision 2026-10-21).
- A global ordering guarantee across ordering keys.
- Staff state changes going through the bus: their audit entry stays in the change's own transaction
  (`add-audit-record` D1, owner decision 2026-10-21); moving them onto the bus is the owner's planned
  future direction and is not built here (design D11).
- Re-driving a `failed` delivery, retention or clean-up of outbox rows, more than one API instance, and a
  dead-letter screen.
- Replacing MediatR: commands, queries and in-module notifications stay on MediatR.
- Changing `operations.journey_events`: it stays the query and catch-up store behind `ILiveJourneys`
  (`Last-Event-ID` resume, sequence numbers); the bus does not replace it.

## Impact

- New shared project `SmartRail.SharedKernel.Infrastructure` [A] (outbox entity mapping, publisher,
  dispatcher, in-process PostgreSQL transport); `SmartRail.SharedKernel.Application` gains the
  abstractions. The Shared Kernel is therefore four projects.
- New tables `outbox_messages` and `outbox_deliveries` in each publishing module's schema: `identity`,
  `network` and `operations`.
- `JourneyGenerated`, `JourneyStateChanged` and `AccessDenied` no longer publish through MediatR;
  their handlers become `IEventConsumer<TEvent>` implementations.
- The `SmartRail.ArchitectureTests` dependency rules are extended for the new project.

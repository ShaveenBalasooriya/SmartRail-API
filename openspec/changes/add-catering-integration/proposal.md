# Proposal: Catering provider integration

## Why

The catering provider is a separate external system that must time food preparation against train
journeys, but today it acts on late or incomplete data and cannot tell a railway-caused failure from
its own (`../docs/user-journeys/08-catering-provider.md`). The brief requires a working integration
with a simulated external platform (§11, §23, M-26) that stays a separate system, so the integration
must be narrow, authenticated and reliable: data it can pull, and changes pushed to it in order,
without duplicates, with retries.

Delivers **EP-12**. Stories: CAT-02 to CAT-12 (CAT-01 is in `add-identity-access`). Depends on
`add-identity-access`, `add-live-running-state`, `add-rail-network-timetable` and `add-event-bus`
(the consumer of `JourneyStateChanged`).

## What Changes

- Provider pull API: stations with platforms, and journeys calling at a station on a date with
  scheduled and expected arrival, platform, status and freshness.
- Webhook subscription management for the calling provider.
- Webhook delivery of delay, cancellation, early-termination, platform-change and correction events: one delivery per
  event per subscription, with event ID, per-journey sequence number, retry with backoff and failure
  counting.
- A simulated provider application, a separate process that registers a webhook, applies events by
  the ordering rules and shows incoming journeys and received events.
- Seed data for the provider and its subscription defaults.

## Capabilities

### New Capabilities

- `provider-data-api`: the provider's authenticated read access to stations, platforms and journeys.
- `webhook-subscriptions`: how a provider registers and manages its callback.
- `webhook-delivery`: pushing changes reliably, in order, without duplicates.
- `simulated-provider`: the stand-in provider application and its processing rules.

### Modified Capabilities

None.

## Out of Scope

- Food ordering, order callbacks, menu feed, preparation times, payment, passenger data of any kind
  (deferred; M-27, M-26 stripped).
- Signed webhooks (owner lean cut), reconciliation snapshots, stale-feed alerting, acceptance
  timeouts (S-17).
- Multiple providers or a provider administration screen.
- Departure events — the provider is told about changes, not about each departure.
- A message broker (NATS JetStream is expected later; owner decision 2026-10-21): deliveries are created by a consumer of the event bus from `add-event-bus`.

## Impact

- New `catering` schema tables `providers`, `webhook_subscriptions`, `webhook_deliveries`.
- A bus consumer, `catering.webhook-deliveries`, registered for `JourneyStateChanged` (event delivery:
  owner decision 2026-10-21, `add-event-bus`).
- `SmartRail.Catering.Contracts`: `ICateringProviderDirectory` replaces the stub from
  `add-identity-access`.
- A second runnable project `SmartRail.SimulatedProvider` and its Compose service.
- Endpoints under `/api/v1/provider`.

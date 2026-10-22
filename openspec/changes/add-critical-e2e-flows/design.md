# Design: Critical end-to-end flows and publish-latency metric

## Context

By this point every module exists. The E2E suite exercises them only through HTTP and SSE against the
Compose stack from `init-backend-foundation`, with tokens from Authentik (blueprint from
`add-identity-access`) and the simulated provider (`add-catering-integration`).

## Goals / Non-Goals

**Goals:** five deterministic flows that fail loudly and diagnosably; one honest latency metric.

**Non-Goals:** UI automation, load tests.

## Decisions

### D1. Test project
`tests/SmartRail.EndToEndTests` (xUnit) using `HttpClient`, an SSE reader helper and an
`AuthentikTokenSource` that obtains tokens for the three seeded staff accounts with the client
credentials / app-password flow. The API base URL, Authentik URL and simulated provider URL come from
environment variables.

### D2. Determinism and isolation
Flows pick a journey from the *seeded test timetable* (a dedicated service per flow, so flows never
share a journey) and use unique client IDs per run. Time-dependent rules use journeys dated today in
the railway zone; the suite's test timetable has departures spread across the day so at least one
upcoming journey per flow always exists. The suite runs against a freshly started stack (CI always
starts one; locally, `docker compose down -v` first). There is no reset endpoint: a fresh stack is
simpler and adds no test-only code to the API.

### D3. Waiting for delivery
Helpers poll with a 5 s budget and 100 ms steps (or wait on the SSE stream for the expected event ID)
instead of fixed sleeps.

### D4. CI wiring
A second workflow `e2e.yml`: `docker compose up -d --wait`, run the suite, upload logs and the
compose logs on failure, `docker compose down -v`. Triggered by pull requests into `main` and
`workflow_dispatch`. Making it a required check on `main` is a repository setting the owner turns
on; it is not part of this change.

### D5. Latency metric
The start time is the persisted event's own `recorded_at` (written in the event's transaction from
`TimeProvider`), not a stamp on the `JourneyStateChanged` message: the event bus delivers at least once and
may deliver late or after a retry (owner decision 2026-10-21, `add-event-bus`), so a consumer that
receives it later is still measured from when the change was made [A]. The stream writer
records `now - recorded_at` into an
OpenTelemetry `Histogram<double>` `smartrail.publish_latency_ms` tagged `channel` and `event_type`
after the frame is flushed; the webhook dispatcher records it on a 2xx. Exported through the Sentry
exporter registered by this change (task 1.1): `Sentry.AspNetCore` with `SendDefaultPii = false`, a
`SetBeforeSend` that strips request headers, cookies and the client IP, and
`AddOpenTelemetry().WithMetrics(...)`. An empty DSN disables Sentry.

### D6. Quality documents
`docs/quality/test-strategy.md`, `functional-scenarios.md`, `defect-log.md` and
`quality-assessment.md` in the docs repository. The functional scenarios map the main spec scenarios to an
automated test, a manual check or a stated gap.

## Risks / Trade-offs

- [Flaky timing in CI] → Event-based waits and a generous budget; flows record timings in the test
  output.
- [A re-run on a used stack can fail because a flow's journey was already changed] → Run on a fresh
  stack; CI always does.

## Open Questions

None.

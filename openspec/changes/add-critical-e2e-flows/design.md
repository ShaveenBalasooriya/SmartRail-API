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
upcoming journey per flow always exists. Re-running is safe because logs use fresh IDs and
changes are corrected or target a fresh journey; the suite starts by calling a Development-only
`POST /dev/reset-journey/{id}` endpoint disabled outside the E2E environment.

### D3. Waiting for delivery
Helpers poll with a 5 s budget and 100 ms steps (or wait on the SSE stream for the expected event ID)
instead of fixed sleeps.

### D4. CI wiring
A second workflow `e2e.yml`: `docker compose up -d --wait`, run the suite, upload logs and the
compose logs on failure, `docker compose down -v`. Triggered by pull requests into `main` and
`workflow_dispatch`. Branch protection on `main` requires it.

### D5. Latency metric
At event commit the unit of work stamps `committedAt` (taken from `TimeProvider`) on the
`JourneyStateChanged` notification. The stream writer records `now - committedAt` into an
OpenTelemetry `Histogram<double>` `smartrail.publish_latency_ms` tagged `channel` and `event_type`
after the frame is flushed; the webhook dispatcher records it on a 2xx. Exported through the Sentry
exporter registered in `init-backend-foundation`.

### D6. Quality documents
`docs/quality/test-strategy.md`, `functional-scenarios.md`, `defect-log.md` and
`quality-assessment.md` in the docs repository. The functional scenarios are generated from the
specs' scenarios with their automated test names, so coverage can be audited.

## Risks / Trade-offs

- [Flaky timing in CI] → Event-based waits and a generous budget; flows record timings in the test
  output.
- [Dev-only reset endpoint is a risk] → Registered only when `Environment == E2E`; an architecture
  test fails if it is mapped in any other environment.

## Open Questions

None.

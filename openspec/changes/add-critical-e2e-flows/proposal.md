# Proposal: Critical end-to-end flows and publish-latency metric

## Why

MVP 1 claims one thing: a change made once reaches every channel, labelled, in order and audited. That
claim is only proven by testing the whole stack through its real interfaces, and the brief asks for
five automated end-to-end flows in CI, a test strategy, defects and a quality assessment (brief §18,
§23; EP-16). EP-17 also needs the one metric that shows how fast a change reaches each channel, which
only exists once every channel does.

Delivers **EP-16** and the publish-to-channel latency part of **EP-17**. Depends on all earlier
changes.

## What Changes

- Five automated end-to-end flows against the full Compose stack (API, PostgreSQL, Authentik,
  simulated provider), run in CI on every promotion to `main` and on demand for `dev`.
- A publish-to-channel latency metric, per channel (public stream, Guard stream, provider webhook),
  and a way to read it for a live change.
- A test strategy document, functional test scenarios, a defect log and a final quality assessment.

## Capabilities

### New Capabilities

- `end-to-end-flows`: the five critical flows and how they run.
- `publish-latency-metric`: measuring how quickly a published change reaches each channel.

### Modified Capabilities

None.

## Out of Scope

- Browser or mobile UI tests (the clients are separate applications).
- Load and performance testing, formal accessibility testing with disabled users (EP-16 Out).
- Dashboards and alerting (EP-17 Out), guard sync backlog metric.
- Penetration testing and SIEM (EP-15 Out).

## Impact

- New test project `SmartRail.EndToEndTests` and a CI job.
- `docs/quality/` documents (strategy, scenarios, defect log, assessment) in the docs repository.
- A metric exported through the existing OpenTelemetry/Sentry setup.

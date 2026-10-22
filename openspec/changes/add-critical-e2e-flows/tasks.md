# Tasks

## 0. Foundation tests (already written)

- [ ] 0.1 The foundation's unit, integration and architecture tests were written while it was built and are kept. Do not write them again: run the existing suite and fix only what fails; verify `dotnet test` passes

## 1. Error tracking and latency metric

- [ ] 1.1 Add Sentry with `SendDefaultPii = false`, a before-send scrubber and a disabled mode when there is no DSN, and register OpenTelemetry metrics with the Sentry exporter (moved here from `init-backend-foundation`); verify the app starts without a DSN, and one unit test that the scrubber removes headers, cookies and the IP
- [ ] 1.2 Record `smartrail.publish_latency_ms` (tags `channel`, `event_type`) as `now - recorded_at` of the persisted event, from the stream writers and the webhook dispatcher (D5); verify on the running stack that a published delay produces a reading per channel (EP-17 Done when)

## 2. End-to-end harness

- [ ] 2.1 Create `SmartRail.EndToEndTests` with the Authentik token source, SSE reader, polling helper and per-flow journey selection from a dedicated E2E test timetable; verify the harness obtains a token for each seeded role against `docker compose up`

## 3. The five flows

- [ ] 3.1 Implement flow 1 (delay reaches every channel); verify it passes against the stack
- [ ] 3.2 Implement flow 2 (offline guard logs sync once, replays harmless); verify it passes
- [ ] 3.3 Implement flow 3 (platform change everywhere including the display feed move and provider event); verify it passes
- [ ] 3.4 Implement flow 4 (guard publish and auditor change denied, both audited); verify it passes
- [ ] 3.5 Implement flow 5 (wrong cancellation corrected on all channels, audit keeps both, linked); verify it passes

## 4. CI and quality documents

- [ ] 4.1 Add `.github/workflows/e2e.yml` (fresh stack, run the suite, tear down) for pull requests into `main` and manual runs; verify the workflow file is valid and the suite passes in it
- [ ] 4.2 Write `docs/quality/test-strategy.md` (layers, what each catches, why only five E2E flows) and `functional-scenarios.md` (the main scenarios mapped to an automated test, a manual check or a stated gap); verify each spec capability appears at least once
- [ ] 4.3 Create `docs/quality/defect-log.md` and record defects found while building; verify the log is referenced from the final assessment
- [ ] 4.4 Write `docs/quality/quality-assessment.md` of the final product against the acceptance criteria and the limits of the testing; verify it cites the CI run

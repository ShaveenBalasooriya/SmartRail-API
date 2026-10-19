# Tasks

## 1. Latency metric

- [ ] 1.1 Stamp `committedAt` on `JourneyStateChanged` and record `smartrail.publish_latency_ms` (tags `channel`, `event_type`) from the stream writers and the webhook dispatcher; verify integration tests with a metrics listener: a stream delivery and a 2xx webhook each add a sample, an abandoned delivery adds none, and the tags are only channel and event type

## 2. End-to-end harness

- [ ] 2.1 Create `SmartRail.EndToEndTests` with the Authentik token source, SSE reader, polling helper and per-flow journey selection from a dedicated E2E test timetable; verify the harness obtains a token for each seeded role against `docker compose up`
- [ ] 2.2 Add the E2E-only reset endpoint and the architecture test that it is not mapped in other environments; verify both

## 3. The five flows

- [ ] 3.1 Implement flow 1 (delay reaches every channel); verify it passes against the stack and fails when the provider webhook is disabled
- [ ] 3.2 Implement flow 2 (offline guard logs sync once, replays harmless); verify it passes and that progress uses original timestamps
- [ ] 3.3 Implement flow 3 (platform change everywhere including the display feed move and provider event); verify it passes
- [ ] 3.4 Implement flow 4 (guard publish and auditor change denied, both audited); verify it passes and that state is unchanged
- [ ] 3.5 Implement flow 5 (wrong cancellation corrected on all channels, audit keeps both, linked); verify it passes
- [ ] 3.6 Verify independence: run the suite twice and in reverse order; all five pass each time

## 4. CI and quality documents

- [ ] 4.1 Add `.github/workflows/e2e.yml` and require it for promotion to `main`; verify a deliberately failing flow blocks a test promotion
- [ ] 4.2 Write `docs/quality/test-strategy.md` (layers, what each catches, why only five E2E flows) and `functional-scenarios.md` (every spec scenario mapped to an automated test or a stated gap); verify every requirement in the specs appears at least once
- [ ] 4.3 Create `docs/quality/defect-log.md` and record defects found while building; verify the log is referenced from the final assessment
- [ ] 4.4 Write `docs/quality/quality-assessment.md` of the final product against the acceptance criteria and the limits of the testing; verify it cites the CI run

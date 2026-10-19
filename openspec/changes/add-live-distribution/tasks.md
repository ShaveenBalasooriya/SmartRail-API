# Tasks

## 1. View model and guidance

- [ ] 1.1 Implement `PublicJourneyView` and the builder over `ILiveJourneys` and `INetworkCatalog`; verify unit tests for calling vs non-calling stops, current platform, corrected note and freshness fields
- [ ] 1.2 Implement `GuidanceBuilder` with the fixed templates, the five-minute delay threshold and the next-service lookup; verify unit tests for each scenario in the `disruption-guidance` spec including the last train of the day

## 2. Passenger endpoints

- [ ] 2.1 Add `GET /public/departures?from=&to=`; verify integration tests: direct trains only, skipped station excluded, wrong direction excluded, different lines → empty 200
- [ ] 2.2 Add `GET /public/stations/{id}/departures` with window rules and the "still listed" rules (just departed, cancelled for ten minutes); verify integration tests with a fake clock including the interchange showing both lines and a 12-hour window → 400
- [ ] 2.3 Add `GET /public/journeys/{id}`; verify integration tests: calling stops flagged, progress fields, unknown → 404, platform change and correction visibility
- [ ] 2.4 Verify anonymous behaviour: no cookies, no IP in logs, `no-store` header, rate-limit policy attached

## 3. Station display feed

- [ ] 3.1 Add `GET /public/display/{stationId}/platforms/{number}` with count limits; verify integration tests: only current-platform journeys, re-platformed train moves with a notice on the old platform, unknown platform → 404
- [ ] 3.2 Add the agreement test comparing display and passenger responses for the same journey after a published delay; verify identical times, status and `lastUpdatedAt`

## 4. Streams

- [ ] 4.1 Implement `StreamHub`, the `JourneyStateChanged` handler and the public SSE endpoint with station and journey filters; verify an integration test that a published delay reaches a subscribed client and a filtered client for another station sees nothing
- [ ] 4.2 Implement resume with buffer-then-read-then-drain and the `reset` event; verify integration tests: missed events delivered in order, an event committed during catch-up delivered exactly once, stale ID → `reset`
- [ ] 4.3 Add heartbeats and the slow-consumer cut-off; verify integration tests with a fake clock for heartbeats and a stalled client being dropped while another keeps receiving
- [ ] 4.4 Add the Guard stream scoped by assignment; verify integration tests: own journeys delivered, another guard's not, controller and auditor → 403
- [ ] 4.5 Verify the public stream payload contains no staff data

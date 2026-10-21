# Tasks

## 1. View model and guidance

- [ ] 1.1 Implement `PublicJourneyView` and the builder over `ILiveJourneys` and `INetworkCatalog`; verify unit tests for calling vs non-calling stops and the current platform
- [ ] 1.2 Implement `GuidanceBuilder` with the fixed templates, the five-minute delay threshold and the next-service lookup; verify unit tests for the cancellation and early-termination templates and the last train of the day

## 2. Passenger endpoints

- [ ] 2.1 Add `GET /public/departures?from=&to=`; verify by calling it on the running app with the test seed
- [ ] 2.2 Add `GET /public/stations/{id}/departures` with the window and "still listed" rules; verify by calling it for the interchange and seeing both lines
- [ ] 2.3 Add `GET /public/journeys/{id}`; verify by calling it on the running app
- [ ] 2.4 Set `Cache-Control: no-store`, issue no cookies and attach the public rate-limit policy; verify the response headers

## 3. Station display feed

- [ ] 3.1 Add `GET /public/display/{stationId}/platforms/{number}` with count limits; verify by calling it on the running app

## 4. Streams

- [ ] 4.1 Implement `StreamHub`, the `distribution.stream-hub` consumer of `JourneyStateChanged` (D3) and the public SSE endpoint with station and journey filters, plus heartbeats; verify one integration test that a published fact reaches a subscribed client exactly once
- [ ] 4.2 Implement resume with buffer-then-read-then-drain skipping by event ID (D4) and the `reset` event; verify one integration test that missed events are delivered in order after reconnecting with `Last-Event-ID`
- [ ] 4.3 Add the Guard stream scoped by assignment; verify a clean build (covered by end-to-end flow 1)

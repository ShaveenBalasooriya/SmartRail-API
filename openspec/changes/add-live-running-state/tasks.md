# Tasks

## 1. Calculator

- [ ] 1.1 Implement `Baseline`, `Fact` types and `LiveStateCalculator` in the Live Operations domain; verify unit tests for every scenario in the `live-running-state` spec (propagation, controller-then-guard, guard-then-controller, early running, out-of-order departures, platform change and its correction, cancellation and its correction, completion, never departed, freshness)

## 2. Persistence and creation

- [ ] 2.1 Add the `journey_live_states`, `journey_stop_live_states` and `journey_events` migration (stream-position sequence, unique `(journey_id, sequence_number)`); verify the schema in an integration test
- [ ] 2.2 Handle `JourneyGenerated` by creating live state, and add the start-up reconciliation via `ITimetable`; verify integration tests: generated journey gets live state; deleting a live-state row and restarting recreates it

## 3. Fact sink and event stream

- [ ] 3.1 Implement `IJourneyFactSink` with the row lock, recompute, change detection and event append; verify integration tests: no-op fact appends no event; 10 concurrent facts on one journey give sequence 1–10; positions strictly increase across two journeys
- [ ] 3.2 Publish `JourneyStateChanged` post-commit; verify an integration test that a rolled-back fact produces no event and no announcement
- [ ] 3.3 Add the completion background job; verify an integration test with a fake clock

## 4. Contract

- [ ] 4.1 Implement `ILiveJourneys` (by ID, by date, calling at station in a window, events after position with journey/station filter, oldest retained position); verify integration tests for each query, including snapshot equals stored state

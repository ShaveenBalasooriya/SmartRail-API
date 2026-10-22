# Tasks

## 1. Calculator

- [ ] 1.1 Implement `Baseline`, `Fact` types and `LiveStateCalculator` in the Live Operations domain; verify unit tests for the core rules in the `live-running-state` spec: delay propagation, controller and guard facts in either order, platform change and its correction, cancellation and its correction, completion, freshness

## 2. Persistence and creation

- [ ] 2.1 Add the `journey_live_states`, `journey_stop_live_states` and `journey_events` migration (stream-position sequence, unique `(journey_id, sequence_number)`); verify the migration applies on start-up
- [ ] 2.2 Handle `JourneyGenerated` by creating live state; verify that after start-up every generated journey has live state

## 3. Fact sink and event stream

- [ ] 3.1 Implement `IJourneyFactSink` with the row lock, recompute, change detection and event append; verify one integration test: a fact appends one event with the next sequence number, and a no-op fact appends none
- [ ] 3.2 Publish `JourneyStateChanged` post-commit; verify a clean build
- [ ] 3.3 Add the completion background job; verify a clean build

## 4. Contract

- [ ] 4.1 Implement `ILiveJourneys` (by ID, by date, calling at station in a window, events after position with journey/station filter, oldest retained position); verify a clean build (the queries are exercised by the next changes' endpoints)

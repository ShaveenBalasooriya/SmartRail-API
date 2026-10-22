# Tasks

## 1. Calculator

- [x] 1.1 Implement `Baseline`, `Fact` types and `LiveStateCalculator` in the Live Operations domain; verify unit tests for the core rules in the `live-running-state` spec: delay propagation, controller and guard facts in either order, platform change and its correction, cancellation and its correction, completion, freshness

## 2. Persistence and creation

- [x] 2.1 Add the `journey_live_states`, `journey_stop_live_states` and `journey_events` migration (stream-position sequence, unique `(journey_id, sequence_number)`); IDs from `Guid.CreateVersion7()`; verify the migration applies on start-up
- [x] 2.2 Handle `JourneyGenerated` by creating live state, and backfill live state at start-up for journeys that lack it; verify that after start-up every journey has live state, including one whose announcement was skipped

## 3. Fact sink and event stream

- [x] 3.1 Implement `IJourneyFactSink` with the row lock, recompute, change detection and event append; verify one integration test: a fact appends one event with the next sequence number, and a no-op fact appends none
- [x] 3.2 Publish `JourneyStateChanged` after the sink commits; verify a clean build

## 4. Contract

- [x] 4.1 Implement `ILiveJourneys` (by ID, by date, events after position with optional journey filter, oldest retained position); verify a clean build (the queries are exercised by the next changes' endpoints)

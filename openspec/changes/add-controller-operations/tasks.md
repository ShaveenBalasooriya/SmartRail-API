# Tasks

## 1. Live view

- [x] 1.0 Add `IStaffDirectory` to the Identity contracts and implement it in Identity's Application layer through a query (D6); verify with a clean build and the architecture tests
- [x] 1.1 Add `GET /operations/journeys` and `/operations/journeys/{id}` with the stale flag and change history; verify by calling them as the seeded controller on the running app

## 2. Publishing

- [x] 2.1 Add the `status_changes` migration and the `StatusChange` domain type; verify the migration applies on start-up
- [x] 2.2 Implement the shared write pipeline (lock, validate, insert, sink, audit, commit); verify one integration test that a failure at the audit step rolls everything back
- [x] 2.3 Implement the delay endpoint with range and departed-stop rules; verify unit tests for the range and departed-stop rules
- [x] 2.4 Implement the cancellation endpoint; verify by cancelling a scheduled journey on the running app
- [x] 2.5 Implement the platform-change endpoint; verify by changing a platform on the running app
- [x] 2.6 Extend Live Operations for early termination (D7): a `Termination` change kind in the calculator, the `terminated` lifecycle status and `termination_published` event type in the Domain and Contracts enums, and a migration widening `CK_journey_events_event_type`; verify one unit test for the rule (an uncorrected termination gives `terminated` with no expected times after the last served stop, and a corrected one gives `running` again)
- [x] 2.7 Implement the early-termination endpoint with the `termination` change type and the stop rules; verify by ending a running journey on the running app and seeing it `terminated` with one event

## 3. Corrections

- [x] 3.1 Implement `POST /operations/changes/{id}/corrections` with the corrected-flag handling and linked audit entries; verify by correcting a cancellation on the running app (end-to-end flow 5 covers it fully)

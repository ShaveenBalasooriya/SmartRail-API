# Tasks

## 1. Live view

- [ ] 1.1 Add `GET /operations/journeys` and `/operations/journeys/{id}` with the stale flag and change history; verify by calling them as the seeded controller on the running app

## 2. Publishing

- [ ] 2.1 Add the `status_changes` migration and the `StatusChange` domain type; verify the migration applies on start-up
- [ ] 2.2 Implement the shared write pipeline (lock, validate, insert, sink, audit, commit); verify one integration test that a failure at the audit step rolls everything back
- [ ] 2.3 Implement the delay endpoint with range and departed-stop rules; verify unit tests for the range and departed-stop rules
- [ ] 2.4 Implement the cancellation endpoint; verify by cancelling a scheduled journey on the running app
- [ ] 2.5 Implement the platform-change endpoint; verify by changing a platform on the running app

## 3. Corrections

- [ ] 3.1 Implement `POST /operations/changes/{id}/corrections` with the corrected-flag handling and linked audit entries; verify by correcting a cancellation on the running app (end-to-end flow 5 covers it fully)

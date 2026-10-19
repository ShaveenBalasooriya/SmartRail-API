# Tasks

## 1. Live view

- [ ] 1.1 Add `GET /operations/journeys` and `/operations/journeys/{id}` with the stale flag and change history; verify integration tests: all of today's journeys in departure order, stale flag at 15 vs 2 minutes with a fake clock, guard and auditor → 403

## 2. Publishing

- [ ] 2.1 Add the `status_changes` migration and the `StatusChange` domain type; verify the schema and the allowed change types in a unit test
- [ ] 2.2 Implement the shared write pipeline (lock, validate, insert, sink, audit, commit); verify an integration test that a failure at the audit step rolls everything back
- [ ] 2.3 Implement the delay endpoint with range and departed-stop rules; verify integration tests for every scenario in the `status-publication` spec (effect on later stops, departed stop → 409, 0 and 721 → 400, tomorrow → 400, completed → 409)
- [ ] 2.4 Implement the cancellation endpoint; verify integration tests: scheduled → cancelled, running journey → 409
- [ ] 2.5 Implement the platform-change endpoint; verify integration tests: valid, other station's platform → 400, same platform → 409, change time equals publish time
- [ ] 2.6 Verify provenance, attribution, immediate event and audit before/after in an integration test across the three change types, and that guard and auditor tokens get 403 with a denied audit entry

## 3. Corrections

- [ ] 3.1 Implement `POST /operations/changes/{id}/corrections` with the corrected-flag handling; verify integration tests: wrong cancellation restored, later platform change survives correcting an earlier one, corrected delay returns to departures-only expectations, correcting twice or a correction → 409, unknown → 404
- [ ] 3.2 Verify the correction event references the original event and the audit entries are linked with the original unchanged

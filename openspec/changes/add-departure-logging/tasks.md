# Tasks

## 1. Guard journey views

- [ ] 1.1 Add `GET /guard/journeys` and `GET /guard/journeys/{id}` over `ILiveJourneys` filtered by the current guard; verify integration tests: only own journeys, empty list, another guard's journey → 404, controller → 403, a published platform change appears with its time (using a test-only fact)

## 2. Departure log write path

- [ ] 2.1 Add the `departure_logs` migration and the `DepartureLog` domain type with the fixed delay-cause enum; verify the schema and a unit test of the cause list
- [ ] 2.2 Implement validation (calling stop, not terminus, offset present, ≤ 2 min ahead, not before service date, time order against neighbouring logged stops, journey dated today or yesterday); verify one unit test per rule
- [ ] 2.3 Implement `POST /guard/departure-logs` with replay/conflict handling, assignment check (403 + `AccessDenied`), fact application, audit entry; verify integration tests: 201, identical replay → 200 with no new event or audit entry, same ID different stop → 409, double tap with two IDs → one 201 and one 409, other guard → 403 with a denied audit entry, cancelled journey → 201 and still cancelled
- [ ] 2.4 Verify the offline scenario end to end at API level: two logs with earlier device times posted later produce actual departures equal to the device times and progress computed from them

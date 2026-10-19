# Tasks

## 1. Guard journey views

- [ ] 1.1 Add `GET /guard/journeys` and `GET /guard/journeys/{id}` over `ILiveJourneys` filtered by the current guard; verify by calling them as the seeded guard on the running app

## 2. Departure log write path

- [ ] 2.1 Add the `departure_logs` migration and the `DepartureLog` domain type with the fixed delay-cause enum; verify the migration applies on start-up
- [ ] 2.2 Implement validation (calling stop, not terminus, offset present, ≤ 2 min ahead, not before service date, time order against neighbouring logged stops, journey dated today or yesterday); verify unit tests for the future bound and the time-order rule
- [ ] 2.3 Implement `POST /guard/departure-logs` with replay/conflict handling, assignment check (403 + `AccessDenied`), fact application and audit entry; verify one integration test for the sync contract: first post 201, identical replay 200 with no new event, same ID with different content 409

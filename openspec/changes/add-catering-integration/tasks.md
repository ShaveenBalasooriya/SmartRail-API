# Tasks

## 1. Provider data and read API

- [ ] 1.1 Add the `catering` migration (`providers`, `webhook_subscriptions`, `webhook_deliveries`), the provider seeder and the real `ICateringProviderDirectory`; verify the schema, idempotent seeding and an inactive-provider key → 401 integration test
- [ ] 1.2 Add `GET /provider/stations` and `/provider/stations/{id}`; verify integration tests: nine stations with platforms, unknown → 404
- [ ] 1.3 Add `GET /provider/stations/{id}/journeys?date=` over `ILiveJourneys`; verify integration tests: arrival order, terminating journey included, date beyond tomorrow → 400, no staff or passenger field in the JSON, provider key rejected by a staff endpoint, 61st request → 429

## 2. Webhook subscriptions

- [ ] 2.1 Add `PUT/GET/DELETE /provider/webhook` with URL validation and one-per-provider replacement; verify integration tests: https accepted, http and private address rejected in production configuration, allowed in development configuration, replacement abandons pending deliveries
- [ ] 2.2 Verify a subscription receives only events after it became active

## 3. Outbox and dispatcher

- [ ] 3.1 Add the in-transaction outbox handler for delay, cancellation, platform change and correction; verify integration tests: departure → no delivery, cancellation → one per active subscription, double announcement → one row, rolled-back change → no row
- [ ] 3.2 Add the dispatcher (skip-locked polling, per-journey ordering, 10 s timeout, backoff table, abandon after five); verify integration tests against a stub receiver: success, fail-then-succeed, five failures → abandoned, event 5 waits for event 4, other journeys unaffected, restart sends pending
- [ ] 3.3 Add the payload contract and metrics counters; verify a contract test of the JSON shape including `correctsEventId`, and that failure logs hold the status but no payload or query string

## 4. Simulated provider

- [ ] 4.1 Create `SmartRail.SimulatedProvider` (own storage, no SmartRail references) with registration on start-up, `POST /webhook`, de-duplication and sequence rules; verify unit tests: duplicate ignored, lower sequence recorded but not applied, cancellation then correction restores the journey
- [ ] 4.2 Add the kitchen screen (journeys in arrival order, event log with applied / duplicate / out-of-order, live update) and the failure switch; verify by driving it from the integration tests and checking the screen's data endpoint
- [ ] 4.3 Add the simulated provider to `compose.yaml` with the shared API key; verify `docker compose up` shows it registered and listing today's journeys

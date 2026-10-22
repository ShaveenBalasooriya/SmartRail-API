# Tasks

## 1. Provider data and read API

- [ ] 1.1 Add the `catering` migration (`providers`, `webhook_subscriptions`, `webhook_deliveries`), the provider seeder and the real `ICateringProviderDirectory`; verify start-up seeds the provider once
- [ ] 1.2 Add `GET /provider/stations` and `/provider/stations/{id}`; verify by calling them with the provider key on the running app
- [ ] 1.3 Add `GET /provider/stations/{id}/journeys?date=` over `ILiveJourneys`; verify by calling it with the provider key on the running app

## 2. Webhook subscriptions

- [ ] 2.1 Add `PUT/GET/DELETE /provider/webhook` with URL validation and one-per-provider replacement; verify by registering, reading and replacing a URL on the running app

## 3. Delivery records and dispatcher

- [ ] 3.1 Add the `catering.webhook-deliveries` consumer of `JourneyStateChanged` (D1): it loads the event through `ILiveJourneys` and, for delay, cancellation, early termination, platform change and correction, inserts one delivery row per active subscription (`ON CONFLICT DO NOTHING`); verify one integration test: a published cancellation creates one delivery row per active subscription, a departure creates none, and the same message delivered twice still leaves one row
- [ ] 3.2 Add the dispatcher (skip-locked polling, per-journey ordering, 10 s timeout, backoff table, abandon after five); verify one integration test against a stub receiver: fail-then-succeed is delivered, and a later event for the same journey waits for the earlier one
- [ ] 3.3 Add the payload contract and the delivery metrics counters; verify a clean build

## 4. Simulated provider

- [ ] 4.1 Create `SmartRail.SimulatedProvider` (in-memory state, no SmartRail references) with registration on start-up, `POST /webhook`, de-duplication and sequence rules; verify unit tests: duplicate ignored, lower sequence recorded but not applied, cancellation then correction restores the journey
- [ ] 4.2 Add `GET /state`, the static kitchen page and the failure switch; verify by opening the page while publishing a delay
- [ ] 4.3 Add the simulated provider to `compose.yaml` with the shared API key; verify `docker compose up` shows it registered and listing today's journeys

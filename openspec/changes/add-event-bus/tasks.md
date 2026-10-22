# Tasks

## 1. Bus infrastructure

- [ ] 1.1 Add the abstractions to `SmartRail.SharedKernel.Application` (`IIntegrationEventPublisher`, `IEventConsumer<TEvent>`, `IEventTransport`) and the new `SmartRail.SharedKernel.Infrastructure` project (D8) with the outbox entity mapping, the `DbContext`-backed publisher and the DI registration extensions; extend `SmartRail.ArchitectureTests` so no module Domain, Application or Contracts project references the new project; verify a clean build and passing architecture tests
- [ ] 1.2 Add the dispatcher `BackgroundService`, the `IOutboxSource` seam, the in-process PostgreSQL transport (delivery rows, per-consumer state, per-key order, backoff, `failed`) and the wake-up plus one-second poll (D3 to D7); verify a clean build and that the application starts with no outbox registered (the first outbox arrives in 2.1)

## 2. Move the three events onto the bus

- [ ] 2.1 Move `AccessDenied` (D9): add the `identity` outbox migration, publish from `AccessDeniedAuthorizationResultHandler` through the publisher, and drop `INotification` from the contract (the `audit.access-denied` consumer arrives with `add-audit-record`); verify by running the application that one refused Guard request leaves one `identity.outbox_messages` row with `published_at` set, and one integration test (restart-redelivers) with a test consumer: a message recorded while the dispatcher is stopped is handled exactly once after the application starts again, and a second start delivers nothing more
- [ ] 2.2 Move `JourneyStateChanged` (D9): add the `operations` outbox migration, publish from `ApplyJourneyFact` in the sink's transaction with the journey event ID and the journey ID as ordering key, and drop `INotification` from the contract; verify one integration test (rollback publishes nothing): a fact that fails after its event was appended leaves no outbox row and no journey event, and an applied fact leaves exactly one; the first real consumers arrive with `add-live-distribution` and `add-catering-integration`, which verify end to end
- [ ] 2.3 Move `JourneyGenerated` (D9): add the `network` outbox migration, publish from `GenerateJourneysCommandHandler` in the same transaction as the inserted journeys, and turn `JourneyGeneratedHandler` into the `live-operations.journey-generated` consumer (idempotent as built); verify by running the application: after start-up generation every journey has live state, each was created once, and a second start publishes nothing new

## 3. Observability

- [ ] 3.1 Add the delivery-failure counters labelled by event type and consumer name and the failed-attempt log entry (D10); verify a clean build and, with a consumer forced to throw on the running application, that the log shows the event ID and consumer name and no payload and that `dotnet-counters monitor` shows the failure counter rising (the exporter arrives with `add-critical-e2e-flows`)

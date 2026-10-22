# Implementation plan: add-event-bus

Planner/reviewer: Claude Sonnet 5.5 (medium). Coding is delegated to OpenCode (`opencode-go/deepseek-v4.1-flash`), one spec task per OpenCode task, in order. Nothing is committed; changes stay in the working tree. The specs are not edited; gaps are listed at the end.

## Design choices that the spec leaves open (decided here, to be reviewed by the owner)

- **Which `DbContext` a publisher writes to.** `IIntegrationEventPublisher` is one interface but three modules publish. Each publishing module registers a *keyed* scoped publisher (keys `identity`, `network`, `operations`, constants in `SmartRail.SharedKernel.Application`), backed by that module's `DbContext`; handlers take `[FromKeyedServices(...)]`. `Publish(event, eventId, orderingKey?)` only adds the tracked outbox row; the caller's existing unit of work saves it.
- **Identity has no unit of work.** `IdentityDbContext` gets an `IUnitOfWork` in `IdentityAccess.Application`, as Rail Network and Live Operations already have.
- **Event type name.** `outbox_messages.event_type` is the contract record's simple name (`JourneyGenerated`, ...); the transport maps names to CLR types from the registered `IEventConsumer<T>` set. An event with no registered consumer gets no delivery rows and is marked published.
- **`IOutboxSource`.** Each module registers one source naming its `DbContext` type and schema; the dispatcher opens a scope and resolves that context.
- **Wake-up.** A singleton signal (`Channel`) set from a `SavedChanges` hook on the outbox-owning contexts; a 1 s poll as fallback.

## Task order, files, risks, verification

### 1.1 Abstractions, Infrastructure project, publisher, architecture tests
- New: `src/SmartRail.SharedKernel.Infrastructure/*` (outbox entity + `ModelBuilder` extension, keyed publisher, DI extensions); abstractions in `SmartRail.SharedKernel.Application` (`IIntegrationEventPublisher`, `IEventConsumer<TEvent>`, `IEventTransport`).
- Touch: `SmartRail.sln`, `Dockerfile` (restore stage lists each Shared Kernel csproj; the new one MUST be added or the image build fails), `tests/SmartRail.ArchitectureTests/DependencyRuleTests.cs` (no module Domain/Application/Contracts references the new project).
- No new NuGet package (EF Core and Npgsql are already central packages); say so.
- Risk: Application projects must not get EF; keep EF types only in the Infrastructure project.
- Verify: `dotnet build SmartRail.sln -warnaserror`; `dotnet test tests/SmartRail.ArchitectureTests`; I check the csproj references by hand.

### 1.2 Dispatcher, `IOutboxSource`, PostgreSQL transport
- New in `SmartRail.SharedKernel.Infrastructure`: dispatcher `BackgroundService`, `IOutboxSource`, transport (delivery rows `ON CONFLICT DO NOTHING`, per-consumer state, per-key order, backoff 2 s/10 s/1 min/5 min, `failed` after 5th), wake-up signal. Host registers the bus in `HostComposition.cs`.
- Risk: no module has an outbox yet, so the dispatcher must tolerate zero sources; the per-key ordering query is the subtle part (a later message waits while an earlier one for the same consumer and key is `pending`; `failed` releases).
- Verify: clean build; `docker compose up --build -d api`, container healthy and logs show no dispatcher error. The ordering/backoff logic is exercised for real by the 2.1 test.

### 2.1 AccessDenied onto the bus
- Touch: `SmartRail.IdentityAccess.Contracts/AccessDenied.cs` (drop `INotification`), `IdentityDbContext` (outbox mapping, `IUnitOfWork`), Identity `DependencyInjection` (keyed publisher, outbox source), new `identity` migration (`dotnet ef migrations add AddOutbox`, dotnet-ef 10.0.9 is installed), `AccessDeniedAuthorizationResultHandler.cs` (resolve the scoped publisher and unit of work from `context.RequestServices`, because the handler is a singleton), test project: `AccessDeniedRecorder` and `StaffAuthorizationTests` currently subscribe via MediatR `INotificationHandler<AccessDenied>` and must be moved to an `IEventConsumer<AccessDenied>` or an outbox query.
- New integration test (restart-redelivers) with a test consumer.
- Risk: the handler now writes to the database before returning 403 (see gap 3). Migrations must apply through the existing `DatabaseMigrator`.
- Verify: build + tests; the restart test; the "refused Guard request leaves one row with `published_at`" check is done through the test `TestEndpoints` in the integration factory (gap 2), plus a SQL check of the row.

### 2.2 JourneyStateChanged onto the bus
- Touch: `JourneyStateChanged.cs` (drop `INotification`), `LiveOperationsDbContext`, Live Operations `DependencyInjection`, new `operations` migration, `ApplyJourneyFactCommand.cs` (publish with the journey event ID and journey ID as key, before the save inside the transaction; remove `IPublisher`), `JourneyFactSinkTests.cs` if it listens for the MediatR notification.
- New test: rollback publishes nothing; an applied fact leaves exactly one row.
- Verify: build + the new test + the existing `JourneyFactSinkTests`.

### 2.3 JourneyGenerated onto the bus
- Touch: `JourneyGenerated.cs` (drop `INotification`), `RailNetworkDbContext`, Rail Network `DependencyInjection`, new `network` migration, `GenerateJourneysCommandHandler.cs` (publish for each created journey before `SaveChangesAsync`), `JourneyGeneratedHandler.cs` → `IEventConsumer<JourneyGenerated>` named `live-operations.journey-generated`, registered in Live Operations Infrastructure; `JourneyFactSinkTests.cs` publishes `JourneyGenerated` through MediatR and needs updating.
- Risk: no seed data (owner constraint), so generation yields zero journeys on the running app. Verification uses an integration test that creates its own line/service/stops, then checks that every journey has live state once and a second start publishes nothing new. The running app is also started to confirm a clean boot.
- Verify: build + that test + app start.

### 3.1 Observability
- Touch: dispatcher/transport in `SmartRail.SharedKernel.Infrastructure` (`Meter`, counters `smartrail.events.delivery_failures` and `smartrail.events.deliveries_failed`, tags only `event_type` and `consumer`; failed-attempt log with event ID, consumer, attempt, exception type, no payload).
- Verify: clean build; a test consumer that throws captures the log (no payload) and a `MeterListener` sees the counter; `dotnet-counters` against the container if it is installed (not installed globally at plan time; otherwise reported as skipped, with the MeterListener evidence instead).

## Review procedure after each OpenCode task
Read the diff, `dotnet build SmartRail.sln -warnaserror`, run the affected tests, rebuild and start the api container where the task says so; send failures back to OpenCode with the exact error; `openspec validate add-event-bus --strict` only if a spec file changes (none planned). Tick tasks.md only after the verify passes.

## Gaps and contradictions found in the spec (reported, not fixed)
1. **One publisher interface, three DbContexts.** D1 does not say how `IIntegrationEventPublisher` picks the module's context. Decided above (keyed registration).
2. **Task 2.1 "verify by running ... one refused Guard request".** The running app has no state-changing endpoint at all (no `MapPost/Put/Patch/Delete` in `src`); only the integration test project's `TestEndpoints` has one. The check can only be done in the test host.
3. **AccessDenied now touches the database inside the authorization handler.** If the outbox write fails the request ends 500 instead of 403. Previously it was an in-process publish. The spec accepts the transactional publish and forbids defensive hardening, so it is built as specified; flagging it.
4. **Task 2.3 "after start-up generation every journey has live state"** cannot be checked on the running app: no network or timetable seed exists (owner constraint). Done in a test with its own data.
5. **Events with no consumer.** A published event with no registered consumer is marked published and never delivered to a consumer that is added later (e.g. denials recorded before `add-audit-record` lands). Not addressed by the spec.
6. **Dockerfile** lists the Shared Kernel projects one by one; the task list does not mention it, but the new project breaks the image build without it.
7. **Task 3.1 verify** needs `dotnet-counters`, which is not installed here.
8. **Design open questions remain open** (fourth Shared Kernel project; failed deliveries not re-driven). Built as written; the owner should confirm.
9. **Contracts still reference the MediatR package** after `INotification` is dropped; harmless, left alone.

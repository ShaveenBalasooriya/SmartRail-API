# Design: Clean Architecture and CQRS per module

## Context

`../docs/architecture.md` §2 and `AGENTS.md` §3 are the target. This change moves the existing code
to it without changing behaviour: every endpoint keeps its route, status codes and JSON shape, and
the database schema stays identical (an EF Core model diff after the refactor is empty).

## Decisions

### D1. Shared Kernel split
- `SmartRail.SharedKernel` (no dependencies): adds `ValidationResult`, a failed `Result` that carries
  an `Error[]`, plus `ValidationResult<T>`. `Error.Validation` stays the single-error form.
- `SmartRail.SharedKernel.Application` (MediatR, FluentValidation, logging abstractions):
  `ICommand`, `ICommand<TResponse>`, `IQuery<TResponse>` over `IRequest<Result>`/`IRequest<Result<T>>`;
  `ICommandHandler<…>`, `IQueryHandler<…>`; `LoggingPipelineBehavior` (request name, success or error
  code, elapsed ms; no request body, so no PII) and `ValidationPipelineBehavior` (runs every
  `IValidator<TRequest>`, returns a `ValidationResult`/`ValidationResult<T>` without throwing).
- `SmartRail.SharedKernel.Api` (ASP.NET Core): `ResultExtensions.ToProblemDetails(this Result)`.
  `ErrorType` → status: Validation 400, NotFound 404, Conflict 409, Unauthorized 401, Forbidden 403,
  Failure 500. A `ValidationResult` produces 400 with the `errors` object the foundation's
  `api-conventions` spec already defines. Problem details go through the Host's existing
  customisation so `traceId` and `type` stay as they are.

### D2. Module wiring
Each module exposes `Add<Module>Application()`, `Add<Module>Infrastructure(configuration)` and
`Add<Module>Api()`. The current `<Module>Module` classes are replaced. The Host registers MediatR once
over every Application assembly (and Infrastructure assemblies that hold notification handlers, if
any remain), adds the two behaviours in the order logging → validation, registers FluentValidation
validators from the Application assemblies, calls `AddCarter` with the module Api assemblies, and maps
them under `/api/v1` with the existing group tags. Module schema names and `ModuleDatabase`
registration for migrations keep working.

### D3. Rail Network
- **Domain:** `Line`, `Station`, `Platform`, `LineStation`, `Service`, `ServiceStop`, `Journey` get
  private constructors, `static Create(...) → Result<T>` and `<Entity>Errors`. Value objects:
  `LineCode`, `StationCode`, `PlatformNumber`, mapped by value conversion so columns do not change.
  `Journey.Create(service, date, clock)` computes the scheduled departure instant.
  `RailwayClock` stays a port: interface `IRailwayClock` in Application `Abstractions/`, implementation
  in Infrastructure.
- **Application slices:** `Network/GetLines`, `Network/GetStations`, `Network/GetStation`,
  `Timetable/GetService` (backs `ITimetable`), `Timetable/GetScheduledJourneys`,
  `Journeys/GenerateJourneys` (command). Validators: `direction` (outbound or inbound,
  case-insensitive, blank means outbound) and `date` (today or tomorrow). Not found → `Result` with
  `NotFound`.
- **Contracts:** `INetworkCatalog`, `ITimetable` implemented in Application by sending the queries.
  The public types in `SmartRail.RailNetwork.Contracts` do not change.
- **Infrastructure:** `Repositories/` (the `ON CONFLICT DO NOTHING RETURNING id` insert moves here),
  `RailNetworkDbContext : IUnitOfWork`, `Jobs/JourneyGenerationJob` sending `GenerateJourneysCommand`.
  The handler publishes `JourneyGenerated` for created rows only after the save commits.
- **Api:** Carter modules `NetworkEndpoints`, `TimetableEndpoints`.

### D4. Live Operations
- **Domain:** `JourneyLiveState` aggregate (with `JourneyStopLiveState` children) and `JourneyEvent`
  move from Infrastructure to Domain. `JourneyLiveState.Create(baseline, now)` builds the initial state;
  `JourneyLiveState.Apply(facts, now)` runs `LiveStateCalculator`, updates itself, and returns the new
  `JourneyEvent` when the state changed (next sequence number) or nothing when it did not. The
  calculator stays a pure domain service. `JourneyLifecycleStatus` in Domain; Contracts keep their own
  public enum, mapped in Application.
- **Application slices:** `LiveState/CreateLiveState` (handles `JourneyGenerated`),
  `LiveState/BackfillLiveState` (command), `LiveState/ApplyJourneyFact` (command; replaces
  `IJourneyFactSink`), `LiveJourneys/GetLiveJourney`, `LiveJourneys/GetLiveJourneysByDate`,
  `JourneyEvents/GetEventsAfter`, `JourneyEvents/GetOldestPosition`. `ILiveJourneys` implemented in
  Application by sending them. `Abstractions/`: `IUnitOfWork` (with `BeginTransactionAsync` for the
  row lock), `IJourneyFactReader` (unchanged, empty default).
- **Infrastructure:** `Repositories/` hold `GetForUpdateAsync` (`SELECT … FOR UPDATE`) and the
  `ON CONFLICT` creation inserts; `Jobs/LiveStateBackfillJob` sends the backfill command.
  `ApplyJourneyFact` publishes `JourneyStateChanged` after commit.

### D5. Architecture tests
NetArchTest rules over every module: Domain references only Shared Kernel; Application references
no EF Core, ASP.NET Core or Infrastructure; Api references no Infrastructure; no module references
another module's Domain, Application, Infrastructure or Api (only its Contracts); no type named
`*Entity`/entity classes deriving `Entity<>` outside Domain projects.

## Risks / Trade-offs

- [Schema drift while moving entities] → Each module's refactor ends with `dotnet ef migrations add`
  producing an empty migration, which is then deleted.
- [Behaviour drift in endpoints] → The existing integration tests stay unchanged and must pass; the
  rail network endpoints are called on the running app before and after.
- [More projects] → Accepted (owner decision): six Api projects plus two Shared Kernel projects.

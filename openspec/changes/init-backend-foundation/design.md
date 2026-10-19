# Design: Backend foundation

## Context

The repository is empty apart from a README. `../docs/architecture.md` fixes the architecture: a
modular monolith of six modules, three layers per module, a PostgreSQL schema per module with no
cross-schema foreign keys, MediatR for in-process events, Sentry for observability and Docker Compose
for local running. This design turns those decisions into a concrete solution layout. See
`proposal.md` for why the foundation comes first.

## Goals / Non-Goals

**Goals:**
- A solution layout in which the module boundary is enforced by project references, not just by
  convention.
- One command that starts the whole stack locally, and one CI workflow every stacked PR must pass.
- Conventions (errors, JSON, logging, seeding) defined once in the Host and the Shared Kernel, so no
  module redefines them.

**Non-Goals:**
- Any module's domain model, endpoints or tables. Each later change adds its own.
- Production deployment manifests.

## Decisions

### D1. Solution layout

```
SmartRail-API/
  src/
    SmartRail.Host/                       composition root: Program.cs, middleware, module registration
    SmartRail.SharedKernel/               Entity, ValueObject, IDomainEvent, Result, paging, TimeProvider helpers
    Modules/
      IdentityAccess/  SmartRail.IdentityAccess.{Domain,Application,Infrastructure}
      RailNetwork/     SmartRail.RailNetwork.{Domain,Application,Infrastructure}
      LiveOperations/  SmartRail.LiveOperations.{Domain,Application,Infrastructure}
      Distribution/    SmartRail.Distribution.{Application,Infrastructure}
      Catering/        SmartRail.Catering.{Domain,Application,Infrastructure}
      Audit/           SmartRail.Audit.{Domain,Application,Infrastructure}
    Contracts/
      SmartRail.<Module>.Contracts        public query interfaces and integration-event types only
  tests/
    SmartRail.<Module>.UnitTests          Domain and Application, no database
    SmartRail.<Module>.IntegrationTests   WebApplicationFactory against PostgreSQL in Testcontainers
    SmartRail.ArchitectureTests           enforces the reference rules below
  seed/                                   JSON seed files, one folder per module
  compose.yaml
  .github/workflows/ci.yml
```

Reference rules: a module's Application layer may reference its own Domain, the Shared Kernel and
other modules' **Contracts** projects only. It may never reference another module's Domain,
Application or Infrastructure. Infrastructure references only its own Application layer. The Host
references every Infrastructure project. `SmartRail.ArchitectureTests` (NetArchTest) fails the
build if any rule is broken. *Alternative considered:* a single project with folders per module.
Rejected, because nothing would stop one module's code from referencing another's tables, which is
exactly what the architecture forbids.

Distribution keeps no data, so it has no Domain project and no DbContext.

### D2. One DbContext per module, one schema each

Each data-owning module has its own `DbContext`, set to its schema with `HasDefaultSchema("<schema>")`
and its own migrations history table (`<schema>.__ef_migrations_history`). All use one database and
one connection string. Migrations are applied when the application starts, in the order `identity`,
`network`, `operations`, `catering`, `audit`. A missing database is created; a failed migration
stops start-up. *Alternative considered:* separate databases per module. Rejected: they would add
infrastructure without adding isolation that separate schemas don't already give.

### D3. Seeding

`seed/<module>/*.json` files are read by an `ISeeder` that each module implements. The Host runs the
seeders after migrations, in dependency order: Identity, then Rail Network, then Catering. Each seeder
upserts by natural key (a station code, a staff username) inside one transaction per module, so a
failure rolls back only that module's seed and stops start-up. Secrets referenced by the seed files
(the provider API key) are read from configuration through `ISecretSource`.

### D4. HTTP conventions in the Host

- Every module maps its endpoints with `app.MapGroup("/api/v1/...")`.
- Errors use `AddProblemDetails` and a single exception handler that adds `traceId` and hides
  exception detail outside Development.
- Validation uses the .NET 10 built-in Minimal API validation (`AddValidation`), so no extra
  validation library is needed.
- JSON uses `System.Text.Json` with a camelCase naming policy, a snake_case string enum converter and
  UTC `DateTimeOffset` serialisation.
- A small middleware assigns and echoes `X-Correlation-Id` and pushes it into the logging scope.
- `TimeProvider` is injected everywhere instead of reading `DateTime.UtcNow`, so time-dependent rules
  (the journey day, freshness, retry backoff) can be tested.

### D5. Logging and Sentry

- Logging uses the built-in JSON console formatter (`AddJsonConsole`) rather than Serilog. That is
  one less dependency, and Compose collects the console output.
- Request logging uses a custom middleware rather than `UseHttpLogging`, because `UseHttpLogging` can
  record headers and bodies, and this design keeps them out by construction.
- Sentry uses `Sentry.AspNetCore` with `SendDefaultPii = false`, a `SetBeforeSend` that strips request
  headers, cookies and the client IP, and a DSN taken from configuration. An empty DSN disables
  Sentry.
- OpenTelemetry metrics are registered here (`AddOpenTelemetry().WithMetrics(...)`) with the Sentry
  exporter, so that later changes only add their own `Meter` instruments.

### D5a. OpenAPI and Scalar
.NET 10's built-in `Microsoft.AspNetCore.OpenApi` generates the document (`AddOpenApi`, `MapOpenApi`
at `/openapi/v1.json`) and `Scalar.AspNetCore` serves the reference (`MapScalarApiReference` at
`/scalar`). Both are mapped only when `Documentation:Enabled` is true, which defaults to true in
Development and E2E. Endpoints get tags, summaries and access descriptions from endpoint metadata,
added once through the `.RequireStaffRole`/`.AllowAnonymousPublic`/`.RequireProvider` helpers from
`add-identity-access`. An architecture test compares the mapped `/api/v1` endpoints with the document.
*Alternative:* Swashbuckle/Swagger UI. Rejected by the owner in favour of Scalar.

### D6. Local stack (`compose.yaml`)

- **Services:** `api`, `postgres` (17), `authentik-server`, `authentik-worker` and the database
  Authentik itself requires. The simulated catering provider is added later by
  `add-catering-integration`.
- **Health checks:** `api` has a health check on `/health/ready`, and its `depends_on` waits for
  PostgreSQL to be healthy.
- **Secrets:** they come from an untracked `.env` file. A checked-in `.env.example` documents the
  variables.

### D7. CI

GitHub Actions on every pull request and push to `dev` runs `dotnet restore`, then `dotnet build` with
warnings as errors, then `dotnet test`. Testcontainers works on the hosted Linux runner because it has
Docker. The end-to-end job is added by `add-critical-e2e-flows`.

## Risks / Trade-offs

- **[MediatR licensing]** MediatR from version 13 onward is distributed under a commercial licence and
  expects a licence key. → Owner decision (2026-10-19): this is a test project, so the latest MediatR
  is used without a licence key. If the library logs a missing-licence warning at start-up, it is
  tolerated, and a key must be obtained before any real deployment.
- **[Start-up migrations]** Applying migrations on start is risky with several instances. → MVP 1 runs
  one instance. If that changes, move migrations to a separate job.
- **[Authentik weight]** Authentik adds containers and memory to the local stack. → It is accepted by
  the owner's identity decision. The stack documents its minimum resources in the README.
- **[Built-in validation is new in .NET 10]** → If it cannot express a rule, the endpoint validates in
  its handler and returns the same 400 problem-details shape.

## Migration Plan

Greenfield; nothing to migrate. Rolling back means reverting the stacked PRs on `dev`.

## Open Questions

None.

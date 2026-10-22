# Proposal: Clean Architecture and CQRS per module

## Why

The first design merged Infrastructure and endpoints into one layer. In the built code that put
use-case logic (`JourneyGenerator`, `JourneyFactSink`), persistence models (Live Operations entities
in `Infrastructure/Persistence/Entities`) and HTTP in one project, left the Application layers nearly
empty, and left `Result` unused. The owner's standard is Clean Architecture with a MediatR CQRS
pipeline, and `../docs/architecture.md` §2 and `AGENTS.md` §3 now describe it. Rail Network and Live
Operations are still small, so they are moved to it now, before later changes copy the old pattern.

## What Changes

- Shared Kernel gains `ValidationResult`; two new projects: `SmartRail.SharedKernel.Application`
  (CQRS interfaces, logging and validation pipeline behaviours) and `SmartRail.SharedKernel.Api`
  (`ToProblemDetails`).
- Every module gets an Api project and one `Add<Module>…()` extension per layer; the Host maps Carter
  modules under `/api/v1` and registers MediatR with the behaviours.
- Rail Network: entities with `Create` factories, value objects and errors; use cases as commands and
  queries; contracts implemented in Application; repositories and unit of work; endpoints as Carter
  modules returning problem details from `Result`; the generation job sends a command.
- Live Operations: the live-state entities move to Domain as an aggregate that applies facts using the
  calculator; creation, backfill and fact application become commands; the contract becomes queries;
  raw SQL moves into repositories.
- Dependency-rule tests in `SmartRail.ArchitectureTests`.

No route, status code, success response or database schema changes; validation problem details now list their errors.

## Capabilities

### New Capabilities

- `module-architecture`: the enforced layering and request pipeline every module follows.

### Modified Capabilities

None.

## Out of Scope

- Identity, `ICurrentUser`, roles and policies (deferred with `add-identity-access`).
- Aspire and ServiceDefaults (`../docs/architecture.md` §7 keeps Docker Compose and no Aspire).
- Any new feature.

## Impact

- New projects: `SmartRail.SharedKernel.Application`, `SmartRail.SharedKernel.Api`, six
  `SmartRail.<Module>.Api`.
- New packages: Carter, FluentValidation (with its DI extensions).
- `IJourneyFactSink` is replaced by an `ApplyJourneyFact` command; later changes send it.

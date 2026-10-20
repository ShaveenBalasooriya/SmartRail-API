# Tasks

## 1. Building blocks and wiring

- [x] 1.1 Add `ValidationResult` to `SmartRail.SharedKernel`; create `SmartRail.SharedKernel.Application` (CQRS interfaces, logging and validation behaviours) and `SmartRail.SharedKernel.Api` (`ToProblemDetails`); add Carter and FluentValidation to `Directory.Packages.props`; verify a clean build and unit tests for the validation behaviour short-circuit and the `ErrorType` → status mapping
- [x] 1.2 Create `SmartRail.<Module>.Api` for all six modules; replace each `<Module>Module` with `Add<Module>Application`, `Add<Module>Infrastructure`, `Add<Module>Api`; Host registers MediatR with the behaviours, FluentValidation and Carter under `/api/v1`; verify a clean build, all existing tests pass and the rail network endpoints answer as before
- [x] 1.3 Add the dependency-rule tests to `SmartRail.ArchitectureTests` (design D5), with the existing Live Operations entities and Rail Network endpoints temporarily excluded by name; verify they pass

## 2. Rail Network

- [x] 2.1 Domain: `Create` factories, value objects, errors; `IRailwayClock` port; verify a clean build and an empty EF model diff
- [x] 2.2 Application slices, validators and contract implementations; repositories and unit of work; generation job sends `GenerateJourneysCommand`; Carter endpoints; remove the old Infrastructure endpoints, queries and generator; drop the exclusion from the architecture tests; verify all tests pass, an empty EF model diff, and the endpoints on the running app (empty lists, 400 for `direction=sideways` with an `errors` entry, 404 unknown station, 400 date out of range) and journey generation at start-up

## 3. Live Operations

- [x] 3.1 Move the live-state entities to Domain as the `JourneyLiveState` aggregate with `Create` and `Apply`; verify a clean build, the calculator tests pass and an empty EF model diff
- [x] 3.2 Application slices (`CreateLiveState`, `BackfillLiveState`, `ApplyJourneyFact`, the four contract queries) and `ILiveJourneys` implementation; repositories with the lock and `ON CONFLICT` inserts; backfill job sends its command; remove the old Infrastructure services; drop the exclusion from the architecture tests; verify all tests pass (the fact-sink integration test now sends `ApplyJourneyFactCommand`), an empty EF model diff, and that every journey has live state after start-up

# Spec Delta

## Purpose

Fixes the internal structure every SmartRail module follows, so the dependency rule is enforced by
tests rather than by convention, and every request runs through the same pipeline (`../docs/architecture.md` §2).

## ADDED Requirements

### Requirement: Layer dependencies point inward
Each module SHALL consist of Domain, Application, Infrastructure and Api projects plus a Contracts
project. Domain SHALL reference only the Shared Kernel; Application SHALL NOT reference EF Core,
ASP.NET Core or Infrastructure; Api SHALL NOT reference Infrastructure; and no module SHALL reference
another module's projects other than its Contracts.

#### Scenario: Dependency rule checked
- **WHEN** the architecture tests run
- **THEN** they fail if any project breaks one of these references

### Requirement: Entities live in the Domain layer
Every class deriving from the Shared Kernel `Entity<TId>` SHALL be in a Domain project, and EF Core
SHALL map those entities directly without separate persistence models.

#### Scenario: Entity outside Domain
- **WHEN** an entity type is declared in an Infrastructure project
- **THEN** the architecture tests fail

### Requirement: Use cases run through the pipeline
Every use case SHALL be a command or query sent through MediatR, passing logging and then validation
behaviours before its handler. A request that fails validation SHALL return HTTP 400 problem details
listing every error, without its handler running.

#### Scenario: Invalid direction
- **WHEN** a client requests the network lines with `direction=sideways`
- **THEN** the response is HTTP 400 problem details with an `errors` entry for `direction`

### Requirement: Expected failures are results
Handlers SHALL return expected failures as `Result` errors, and endpoints SHALL map them to problem
details by error type (validation 400, not found 404, conflict 409, unauthorised 401, forbidden 403).

#### Scenario: Unknown station
- **WHEN** a client requests a station ID that does not exist
- **THEN** the handler returns a not-found error and the response is HTTP 404 problem details

### Requirement: Behaviour and schema unchanged
The refactor SHALL NOT change any route, status code, success response body or database schema; a validation failure's problem details SHALL additionally list its errors.

#### Scenario: Model diff
- **WHEN** a new EF Core migration is generated for each module after the refactor
- **THEN** it contains no operations

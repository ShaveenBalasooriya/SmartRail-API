# Proposal: Initialise the backend foundation

## Why

`SmartRail-API` holds only a README. Every MVP 1 epic needs the same base first: a runnable ASP.NET
Core host, the six-module structure from `../docs/architecture.md`, PostgreSQL with a schema per
module, a way to load seed data, health and error conventions, diagnostic logging without personal
data, a local stack, and CI. Without this, each later change would invent its own versions of these.

Delivers the foundation part of **EP-01** (service structure, database and migrations, seed-data
mechanism, CI pipeline). It also delivers the health endpoint and PII-free structured logs from
**EP-17**, and the log rule from **EP-14**. EP-01's domain event log and real-time channel are
delivered later, by `add-live-running-state` and `add-live-distribution`, where they are first used.

## What Changes

- A .NET 10 solution containing the Host (the composition root), a Shared Kernel and six empty module
  skeletons, each with Domain, Application, and Infrastructure & Endpoints projects.
- PostgreSQL wiring: one EF Core `DbContext` per module that keeps data, each limited to its own
  schema, with migrations applied when the application starts.
- A seed-data mechanism that later changes use to load the network, timetable, staff accounts and
  provider record.
- Health endpoints, a consistent error response format, request correlation IDs and JSON
  conventions, all under `/api/v1`.
- Structured logs and error tracking (Sentry), with tokens, keys, client IP addresses and passenger
  personal data kept out.
- An OpenAPI document and the Scalar API reference, enabled in Development and End-to-end
  environments only.
- A Docker Compose stack: the API, PostgreSQL, Authentik and its required services.
- A CI pipeline that builds the solution and runs the unit and integration tests on every pull
  request to `dev`.
- Test projects for unit tests and for module integration tests against real PostgreSQL.

## Capabilities

### New Capabilities

- `service-health`: liveness and readiness endpoints that report whether the API and its database
  can serve requests.
- `api-conventions`: the shared HTTP contract every endpoint follows, covering the base path, JSON
  format, timestamps, error responses, validation errors and correlation IDs.
- `diagnostic-logging`: structured request and error logging and error tracking, with the rules for
  what must never be logged.
- `api-documentation`: the OpenAPI description and the Scalar interactive reference (not Swagger UI).
- `seed-data`: loading the reference data MVP 1 runs on from files kept in the repository, safe to
  repeat on every start.

### Modified Capabilities

None. This is the first change in the repository.

## Out of Scope

- Any railway behaviour: the network, timetable, journeys, departure logs, status changes, streams or
  catering. Each comes in its own later change.
- Authentication and authorisation (`add-identity-access`) and the audit record
  (`add-audit-record`).
- The seed content itself (station names, the timetable and staff accounts). The owner defines it,
  and the changes that own each kind of data load it.
- Deploying anywhere other than the local Docker Compose stack.
- Dashboards and alerting (EP-17 Out of MVP 1).
- Every client application: the operations web, the Guard app, the passenger app and the station
  display.

## Impact

- New repository content: the solution, the projects, `compose.yaml`, the CI workflow, test
  projects and seed folders.
- New dependencies: EF Core with the PostgreSQL provider, MediatR, the Sentry SDK, xUnit and
  Testcontainers.
- Every later change builds on this structure and these conventions.

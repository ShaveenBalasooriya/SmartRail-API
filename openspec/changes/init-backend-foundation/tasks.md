# Tasks

Verification here is limited to a clean build and starting the application. The automated tests for
this change's behaviour are written in the final testing pass (`add-critical-e2e-flows`, section 0),
so initialisation is not slowed down by test work.

## 1. Solution and module skeleton

- [x] 1.1 Create the .NET 10 solution with `SmartRail.Host`, `SmartRail.SharedKernel`, and Domain, Application, Infrastructure and Contracts projects for the six modules, laid out as in design D1 (Distribution has no Domain); verify `dotnet build` succeeds with warnings as errors
- [x] 1.2 Add `Directory.Build.props` (nullable enabled, warnings as errors, analysers) and `Directory.Packages.props` (central package versions, latest MediatR, no licence key); verify a clean build
- [x] 1.3 Add the Shared Kernel base types (`Entity`, `ValueObject`, `IDomainEvent`, `Result`, paging) and register `TimeProvider`; verify a clean build

## 2. Host conventions

- [x] 2.1 Configure the `/api/v1` route groups, camelCase JSON, snake_case enum strings and UTC timestamps; verify the application starts
- [x] 2.2 Add problem details, the global exception handler and the 404 and 415 handling; verify the application starts
- [x] 2.3 Enable the .NET 10 built-in validation; verify the application starts
- [x] 2.4 Add the correlation-ID middleware; verify a request to `/health/live` returns an `X-Correlation-Id` header
- [x] 2.5 Add CORS from configured origins; verify the application starts
- [x] 2.6 Add OpenAPI generation and the Scalar reference, enabled only in Development and E2E; verify `/openapi/v1.json` and `/scalar` load in Development and return 404 in a production configuration

## 3. Persistence and migrations

- [x] 3.1 Add the PostgreSQL connection, and the empty `DbContext` for `identity`, `network`, `operations`, `catering` and `audit`, each with its schema and its own migrations history table; verify the five schemas exist after start-up
- [x] 3.2 Apply migrations on start-up in the D2 order, and stop start-up on failure; verify start-up against an empty database

## 4. Health and logging

- [x] 4.1 Add `/health/live` and `/health/ready` (database plus migrations-applied checks) with sanitised bodies; verify `/health/ready` is 200 with the database running
- [x] 4.2 Add the JSON console logging and the request-logging middleware (method, route template, status, duration, correlation ID); verify a request writes one JSON log line with those fields

## 5. Local stack and CI

- [x] 5.1 Add a multi-stage `Dockerfile` for the API, `compose.yaml` (api, postgres, and Authentik as `postgresql`, `server` and `worker` with no Redis) and `.env.example`; verify `docker compose up` brings the API to `/health/ready` = 200
- [x] 5.2 Add `.github/workflows/ci.yml` (restore, build with warnings as errors, test) for pull requests and pushes to `dev`; verify the workflow file is valid
- [x] 5.3 Write the README section on running the stack and the environment variables; verify by following it

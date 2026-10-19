# Tasks

## 1. Solution and module skeleton

- [ ] 1.1 Create the .NET 10 solution with `SmartRail.Host`, `SmartRail.SharedKernel`, and Domain, Application, Infrastructure and Contracts projects for the six modules, laid out as in design D1 (Distribution has no Domain); verify `dotnet build` succeeds with warnings as errors
- [ ] 1.2 Add `Directory.Build.props` (nullable enabled, warnings as errors, analysers) and `Directory.Packages.props` (central package versions, latest MediatR, no licence key); verify a clean build
- [ ] 1.3 Add `SmartRail.ArchitectureTests` enforcing the reference rules in D1; verify the tests pass, and that adding a forbidden reference makes them fail
- [ ] 1.4 Add the Shared Kernel base types (`Entity`, `ValueObject`, `IDomainEvent`, `Result`, paging) and register `TimeProvider`; verify with unit tests for `Result` and `ValueObject` equality

## 2. Host conventions

- [ ] 2.1 Configure the `/api/v1` route groups, camelCase JSON, snake_case enum strings and UTC timestamps; verify an integration test that an enum and a timestamp serialise as the `api-conventions` spec says
- [ ] 2.2 Add problem details, the global exception handler and the 404 and 415 handling; verify integration tests for an unknown route, an unsupported media type, malformed JSON, and a 500 that hides exception text outside Development
- [ ] 2.3 Enable the .NET 10 built-in validation and add a test-only endpoint to the integration tests; verify that two invalid fields return 400 with both listed in `errors`
- [ ] 2.4 Add the correlation-ID middleware; verify integration tests for an echoed ID, a generated ID and an invalid ID being replaced
- [ ] 2.5 Add CORS from configured origins; verify an unlisted origin receives no allow-origin header

- [ ] 2.6 Add OpenAPI generation and the Scalar reference, enabled only in Development and E2E; verify integration tests: `/openapi/v1.json` is valid and lists a test endpoint with its 400 response, `/scalar` loads, both return 404 in a production configuration, and an architecture test fails when a mapped `/api/v1` endpoint is missing from the document

## 3. Persistence, migrations and seeding

- [ ] 3.1 Add the PostgreSQL connection, and the empty `DbContext` for `identity`, `network`, `operations`, `catering` and `audit`, each with its schema and its own migrations history table; verify an integration test that all five schemas exist after start-up
- [ ] 3.2 Apply migrations on start-up in the D2 order, and stop start-up on failure; verify an integration test against an empty Testcontainers database
- [ ] 3.3 Add the `ISeeder` contract, the ordered seed runner, per-module transactions and `ISecretSource`; verify integration tests: seeding twice gives identical data, a new seed record is inserted, and a malformed file stops start-up with the file name logged
- [ ] 3.4 Create the `seed/` folder structure with a README describing each module's file format (left empty for the owner to fill); verify the folder and README exist

## 4. Health, logging and error tracking

- [ ] 4.1 Add `/health/live` and `/health/ready` (database plus migrations-applied checks) with sanitised bodies; verify integration tests: ready is 200 when healthy, ready is 503 when the database is stopped, live is 200 while the database is down, and no exception text appears in either
- [ ] 4.2 Add the JSON console logging and the request-logging middleware (method, route template, status, duration, correlation ID); verify an integration test that captures the log and asserts the fields
- [ ] 4.3 Add Sentry with `SendDefaultPii = false` and a before-send scrubber, and a disabled mode when there is no DSN; verify the app starts without a DSN, and a unit test that the scrubber removes headers, cookies and the IP
- [ ] 4.4 Add the PII log test from the `diagnostic-logging` spec (a token, an API key and an IP address sent in requests must not appear in captured logs); verify it passes, and fails when a leak is introduced deliberately
- [ ] 4.5 Register OpenTelemetry metrics with the Sentry exporter; verify the app starts and the meter provider resolves

## 5. Local stack and CI

- [ ] 5.1 Add a multi-stage `Dockerfile` for the API, `compose.yaml` (api, postgres, Authentik and its dependencies) and `.env.example`; verify `docker compose up` brings the API to `/health/ready` = 200
- [ ] 5.2 Add `.github/workflows/ci.yml` (restore, build with warnings as errors, test) for pull requests and pushes to `dev`; verify the workflow passes on the first stacked PR
- [ ] 5.3 Write the README section on running the stack, the environment variables and the test commands; verify by following it from a clean clone

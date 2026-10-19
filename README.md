# SmartRail-API

The backend of **SmartRail**: a railway operations and passenger-information platform, built as a
modular monolith on .NET 10. This repository implements the agreed MVP 1 scope.

`AGENTS.md` is the working brief — what the project is, the architecture to follow and the commit
rules. Read it before changing anything. The design decision records for the current change live in
`openspec/changes/`.

## What is in here

| Path | What it holds |
|---|---|
| `src/SmartRail.Host/` | The composition root: `Program.cs`, middleware, health, migrations |
| `src/SmartRail.SharedKernel/` | `Entity`, `ValueObject`, `IDomainEvent`, `Result`, paging |
| `src/Modules/<Module>/` | The six modules, each with Domain, Application and Infrastructure layers |
| `src/Contracts/` | Each module's public query interfaces and integration-event types |
| `tests/` | Unit, architecture and integration tests |
| `compose.yaml` | The local stack: API, PostgreSQL and Authentik |
| `Dockerfile` | Multi-stage build for the API image |

Six modules share one database, one **schema** each:

| Module | Schema |
|---|---|
| Identity & Access | `identity` |
| Rail Network | `network` |
| Live Operations | `operations` |
| Catering Integration | `catering` |
| Audit | `audit` |
| Distribution | none — it serves reads, it stores nothing |

## Prerequisites

- **.NET SDK 10.0.400 or later** (the exact version is pinned in `global.json`).
- **Docker**, with the Compose v2 plugin (`docker compose`), for the local stack and for the
  integration tests, which start a real PostgreSQL container through Testcontainers.
- About **4 GB of RAM free** for the full stack. Authentik runs a server and a worker and is the
  heavy part; API and PostgreSQL alone need well under 1 GB.

## Run the whole stack

1. Create your environment file and fill in the three required secrets:

   ```bash
   cp .env.example .env
   ```

   `.env` is untracked. `.env.example` documents every variable; the required ones are:

   - `POSTGRES_PASSWORD` — the password for SmartRail's own database.
   - `AUTHENTIK_SECRET_KEY` — generate with `openssl rand -base64 60 | tr -d '\n'`.
   - `PG_PASS` — the password for Authentik's own PostgreSQL database.

2. Start everything:

   ```bash
   docker compose up --build
   ```

   The first run builds the API image and pulls the PostgreSQL images and the Authentik server,
   worker and database images, so it takes a few minutes. Leave it in the foreground to watch the
   logs, or add `-d` to run in the background.

3. Wait until the API reports ready, then use it:

   ```bash
   curl http://localhost:5080/health/ready      # HTTP 200 with {"status":"Healthy",...}
   ```

   | Service | URL |
   |---|---|
   | API | <http://localhost:5080> |
   | Liveness | <http://localhost:5080/health/live> |
   | Readiness | <http://localhost:5080/health/ready> |
   | API reference (Scalar) | <http://localhost:5080/scalar> (Development only) |
   | OpenAPI document | <http://localhost:5080/openapi/v1.json> (Development only) |
   | Authentik | <http://localhost:9000> |
   | PostgreSQL | `localhost:5432` |

   The API applies every module's migrations on start-up. A failed migration stops start-up
   rather than serving from a half-built database, and the log names the module at fault.

4. Stop it, and remove the database volumes when you want a clean start:

   ```bash
   docker compose down          # stop and remove the containers
   docker compose down -v       # also delete the PostgreSQL volumes
   ```

## Environment variables

Compose reads them from `.env`; the API reads them from the environment or from `appsettings.json`.
The names below are the ones that matter day to day — `.env.example` is the full list.

| Variable | Default | Purpose |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Development` | Enables the API reference and OpenAPI in Development/E2E |
| `API_PORT` | `5080` | Host port for the API |
| `POSTGRES_PORT` | `5432` | Host port for SmartRail's PostgreSQL |
| `POSTGRES_DB` / `POSTGRES_USER` | `smartrail` | SmartRail's database name and owner |
| `POSTGRES_PASSWORD` | — | **Required.** Password for that database |
| `ConnectionStrings__Database` | set by Compose | The API's PostgreSQL connection string |
| `OPERATIONS_WEB_ORIGIN`, `PASSENGER_WEB_ORIGIN`, `STATION_DISPLAY_ORIGIN` | localhost values | The CORS allow-list |
| `DOCUMENTATION_ENABLED` | `true` | Serve `/openapi/v1.json` and `/scalar` |
| `AUTHENTIK_SECRET_KEY` | — | **Required.** Authentik's signing key |
| `PG_DB` / `PG_USER` | `authentik` | Authentik's PostgreSQL database name and owner |
| `PG_PASS` | — | **Required.** Password for Authentik's PostgreSQL database |
| `COMPOSE_PORT_HTTP` / `COMPOSE_PORT_HTTPS` | `9000` / `9443` | Host ports for Authentik's HTTP and HTTPS listeners |
| `AUTHENTIK_IMAGE` | `ghcr.io/goauthentik/server` | The Authentik image repository |
| `AUTHENTIK_TAG` | `2026.8.3` | The Authentik image tag; must be a published tag on `ghcr.io/goauthentik/server` |

## Run the API without Docker

You still need PostgreSQL reachable at the connection string the API uses. The default in
`appsettings.json` is:

```
Host=localhost;Port=5432;Database=smartrail;Username=smartrail;Password=smartrail
```

Start one and run the API:

```bash
docker run --rm -p 5432:5432 -e POSTGRES_PASSWORD=smartrail -e POSTGRES_USER=smartrail -e POSTGRES_DB=smartrail postgres:17-alpine

# in another shell, from the repository root
ConnectionStrings__Database="Host=localhost;Port=5432;Database=smartrail;Username=smartrail;Password=smartrail" \
  dotnet run --project src/SmartRail.Host
```

The API then listens on <http://localhost:5080> (the `applicationUrl` in
`src/SmartRail.Host/Properties/launchSettings.json`).

## Build and test

```bash
dotnet build SmartRail.sln -warnaserror     # warnings are errors in Directory.Build.props
dotnet test SmartRail.sln                   # every test project
```

The test projects are:

| Project | What it covers |
|---|---|
| `tests/SmartRail.SharedKernel.UnitTests` | `Result`, `ValueObject`, `Entity`, paging — no database |
| `tests/SmartRail.Host.UnitTests` | JSON conventions and correlation IDs |
| `tests/SmartRail.ArchitectureTests` | The design D1 reference rules (reads the project files) |
| `tests/SmartRail.Host.IntegrationTests` | The real Host against a real PostgreSQL in Testcontainers |

**The integration tests need Docker.** They start one PostgreSQL container for the run and start the
real application against it, so they exercise migrations and the health endpoints rather
than a substitute. Run a subset while you work:

```bash
dotnet test SmartRail.sln --filter "FullyQualifiedName~Persistence"
dotnet test SmartRail.sln --filter "FullyQualifiedName~Health"
dotnet test tests/SmartRail.ArchitectureTests/SmartRail.ArchitectureTests.csproj
```

## Continuous integration

`.github/workflows/ci.yml` runs on every pull request and every push to `dev`: restore, build with
`-warnaserror`, then the full test suite. It runs on `ubuntu-latest`, which provides the Docker
daemon the integration tests need.

# AGENTS.md — SmartRail-API

This file is the working brief for any AI agent (or human) contributing to **this repository**.
Read it before you change anything. It covers what the project is, what it has to do, how the code
must be structured, and the rules for committing.

---

## 1. What this repository is

`SmartRail-API` is the **backend** of SmartRail: a railway operations and passenger-information
platform built as an individual, AI-assisted software engineering assessment.

It is one part of a larger piece of work:

| Where | What |
|---|---|
| `../docs/` | The requirements, discovery, scoping, user journeys, architecture and data model. A **separate git repository** (`SmartRail-Docs`). |
| `../final-assessment-brief.md` | The original business brief. Treat it as an initial brief, not a complete specification. |
| **this repo** | The .NET backend that implements the agreed MVP 1 scope. |

The design is already settled and written down. **`../docs/architecture.md` and
`../docs/data-model.md` are the authority for structure**; `../docs/scoping/mvp-1-scope.md` is the
authority for scope. Do not re-litigate decisions that are already recorded there — implement them.
Anything genuinely new or unclear is raised with the owner, not decided silently.

The EP-01 foundation (`init-backend-foundation`) is built: the Host, the Shared Kernel, the module
skeletons, schema-per-module persistence, health, logging, OpenAPI with Scalar, Docker Compose and CI.
Later work follows the OpenSpec changes in `openspec/changes/`, in the build order below.

---

## 2. What SmartRail is supposed to do

### The problem

Passengers cannot see what is happening across the railway, and do not have accurate, up-to-date
information to help them travel. Operations staff work from fragmented views with no shared source
of truth. The root cause named across the discovery work is the same everywhere: **there is no
shared integration layer that collects network state once and distributes it** to everyone who
needs it.

### What MVP 1 proves

MVP 1 proves **one thing end to end: a change to a running train is made once and reaches every
channel.**

- A small network of **two lines, five stations each, sharing one interchange** (nine distinct
  stations), and its timetable, are **seed data**.
- Each day's operational journeys are **generated from the seeded timetable**.
- The **Guard's per-station departure logs** drive live progress. The Guard works offline and the
  logs sync exactly once.
- The **Network Controller** publishes delays, cancellations and platform changes, and corrects
  mistakes without erasing the original.
- Every change propagates from **one source of truth** to the passenger app, a station platform
  display, the Guard's app and the external catering provider — each copy carrying a timestamp and
  a provenance label, **audited**, and **only possible for the right role**.

If a feature does not serve that loop, it is not in MVP 1.

### Roles in MVP 1

| Role | Surface | Does |
|---|---|---|
| Network Controller | Operations web | Sees today's live picture; publishes delay, cancellation and platform change; issues corrections; owns every change to today's journeys |
| Guard | Crew mobile app | Sees assigned journeys; logs the actual departure at every stop with a delay cause from a fixed list; works offline; sees changes to their journey |
| Auditor | Operations web (read-only) | Views the audit record |
| Passenger | Passenger app, station display | Reads live information; **no account** in MVP 1 |
| Catering provider | Machine client (API key) | Pulls journey data and receives pushed status changes |

The Scheduler, Driver, Station Master and System Admin roles are **deferred**. There are no
passenger accounts, no journey planner and no food ordering in MVP 1.

### The five automated end-to-end flows

These are the acceptance bar for the whole build. They must run green in CI:

1. **Delay reaches every channel** — a published delay updates expected times in the passenger app,
   the station display and the Guard's app, and reaches the simulated provider.
2. **Offline guard logs sync once** — departures logged offline sync exactly once with their
   original timestamps, and the app and display show updated progress.
3. **Platform change everywhere** — a platform change appears with its time and provenance label on
   every surface, including the provider.
4. **Separation of duties denials** — a Guard token publishing a disruption and an Auditor token
   making any change both get `403`, and both denials are audited.
5. **Wrong cancellation corrected** — a correction reaches every channel, and the audit keeps the
   original.

---

## 3. Architecture you must follow

These are agreed decisions. Follow them; do not substitute alternatives.

### Modular monolith

One ASP.NET Core process, divided internally into modules with hard boundaries. **Not**
microservices. Each module has **three layers**, not four:

| Layer | Contains | May depend on |
|---|---|---|
| **Domain** | Entities, value objects, domain events, invariants | nothing |
| **Application** | Use-case handlers, orchestration, and *ports* (interfaces for persistence, other modules, external systems) | Domain only |
| **Infrastructure & Endpoints** | EF Core repositories implementing the ports, Minimal API endpoint groups, external HTTP calls | Application, and so Domain |

Dependencies point inward. Domain and Application must never reference EF Core, HTTP or Authentik.

**Minimal APIs, not controllers.** Each module maps its own `MapGroup` inside its own project.
There is no shared `Controllers/` folder.

### The six modules

| Module | Responsibility | Schema |
|---|---|---|
| **Identity & Access** | Authentik integration, token validation, the role catalogue, role enforcement, the catering API key | `identity` |
| **Rail Network** | Seeded lines, stations, platforms, station order, the seeded timetable, daily journey generation | `network` |
| **Live Operations** | Guard departure logs and idempotent sync; live journey state, expected times, freshness, lifecycle; Controller changes and corrections; the journey event stream | `operations` |
| **Distribution** | The public read API and the live SSE streams, including reconnect and catch-up | none |
| **Catering Integration** | Provider read API, webhook subscriptions, delivery with sequence numbers, retry and duplicate handling | `catering` |
| **Audit** | Append-only, hash-chained record of every staff change and every denied attempt; the Auditor's read view | `audit` |

Plus two cross-cutting pieces: a **Shared Kernel** (base types, `Result`, pagination — no business
logic, depends on nothing) and a **Host** (composition root; the only project that knows all
modules exist).

### How modules talk

- **Never** query another module's tables.
- **"Something happened"** → an in-process event via **MediatR**; interested modules register their
  own `INotificationHandler`.
- **"What is the current state of X"** → a call to the owning module's **in-process query contract**
  (an interface in its Application layer, resolved from DI).

### Data

- **PostgreSQL**, one database, **one schema per module**, one `DbContext` per module scoped to its
  own schema.
- **No foreign key crosses a schema boundary.** Another module's entity is stored as a plain
  identifier and resolved through that module's contract.
- Identifiers are generated in code with `Guid.CreateVersion7()`, never `Guid.NewGuid()`; the
  correlation ID is the only exception.

### Identity

- **Authentik** is the single identity provider (OIDC/OAuth2). SmartRail never stores a login
  credential.
- **RBAC lives in SmartRail**, not in Authentik. Authentik answers "who is this"; this codebase
  answers "what may they do, and can that be proven".
- In MVP 1 only the **staff** client exists. Staff accounts are seed data in an Authentik
  **blueprint**. The passenger app and station display call public endpoints with no token.
- The catering provider authenticates with an **API key**; only a hash is stored.

### Real-time

- **Server-Sent Events (SSE)** for every live client — passenger app, station display and Guard's
  app. No client sends over the live channel in MVP 1.
- The Guard's departure logs go up as ordinary HTTP requests from the offline queue, not over SSE.
- SignalR is not used in MVP 1.

### Observability and local running

- **Sentry** only, via OpenTelemetry, so one instrumentation serves local and production. **No
  Aspire.** Sentry and the metrics are added by `add-critical-e2e-flows`, not before.
- **Docker Compose** runs the API, PostgreSQL, Authentik and the simulated provider. A `/health`
  endpoint backs Compose health checks.
- Logs must contain **no passenger PII**.

### Tests

Three layers, per EP-16: **unit** (Domain, no database), **module integration**
(`WebApplicationFactory` against real PostgreSQL via Testcontainers), and the **five end-to-end
flows** in Docker Compose in CI.

Keep testing lean:

- Write a test only where the task asks for one. Otherwise a task is verified by a clean build and by
  running the application.
- Unit tests are for domain rules. Integration tests are for a few genuinely risky points
  (transactions, idempotent sync, delivery ordering), never one per spec scenario.
- Do not add architecture tests, coverage checks, concurrency stress tests or fake-clock tests unless
  a task names them.

### Build order

EP-01 foundation → EP-02 identity → EP-03 audit → EP-13 tokens → EP-04 network and timetable →
EP-06/EP-07 guard and live running → EP-08 controller → EP-09/EP-10/EP-11/EP-12 distribution and
channels. EP-14, EP-15, EP-16 and EP-17 run across all of it.

---

## 4. Committing: Graphite only

All commits go through Graphite (`gt`). Never use `git commit` or `git commit --amend` directly.

Load the **graphite** skill for the workflow — `gt create`, `gt modify`, stack navigation,
branch naming, restacking and troubleshooting are all documented there. Don't restate them here.

Repo-specific rules the skill won't know:

- The trunk is **`dev`**, not `main`. `main` only receives promotions from `dev`.
- Never commit to `dev` or `main` directly; work always happens on a stacked branch.
- Do not run `gt submit` unless the owner explicitly asks — it publishes real pull requests.
- Keep PRs small and atomic; more small PRs beats fewer large ones.

---

## 5. Commit message rules

Every commit message **must follow this exact template**:

```text
[<type>]: <title>

<message>

Signed-off-by: Shaveen Balasooriya <shaveen.balasooriya@ascentic.se>
```

The types are:

| Type | Use for |
|---|---|
| `feat` | A new feature |
| `fix` | A bug fix |
| `docs` | Documentation only changes |
| `style` | Changes that do not affect the meaning of the code (white-space, formatting, etc.) |
| `refactor` | A code change that neither fixes a bug nor adds a feature |
| `perf` | A code change that improves performance |
| `test` | Adding missing tests or correcting existing tests |
| `chore` | Changes to the build process or auxiliary tools and libraries |

Shape:

- **Line 1:** `[<type>]: <title>` — square brackets around the type, a colon, then the title.
- **Blank line.**
- **Body:** `<message>` — what changed and why, wrapped at about 72–80 columns.
- **Blank line.**
- **Trailer:** exactly `Signed-off-by: Shaveen Balasooriya <shaveen.balasooriya@ascentic.se>`.

### The signature is the only trailer

- **Never add a `Co-authored-by` trailer**, in any form, for any reason.
- **Never add AI attribution** of any kind — no `Co-authored-by: Claude`, no
  `Generated with ...`, no "written by an AI" footer, no tool name in the body.
- **Never add or alter the `Signed-off-by` line.** It is the owner's signature and the only
  attribution a commit carries. Copy it exactly as shown, including the name and email.

The owner is the sole author of every commit in this repository. Do not set `user.name` or
`user.email` to anything else, and do not add a second sign-off.

### Example

```text
[feat]: Add module skeleton and host composition root

Creates the six module projects with their Domain, Application and
Infrastructure layers, the Shared Kernel, and the Host project that
registers every module's endpoints. No business logic yet; this is the
structure EP-01 needs before anything else can land.

Signed-off-by: Shaveen Balasooriya <shaveen.balasooriya@ascentic.se>
```

A one-line commit with no body is acceptable only when the title says everything:

```text
[chore]: Add .editorconfig

Signed-off-by: Shaveen Balasooriya <shaveen.balasooriya@ascentic.se>
```

---

## 6. Definition of done for a change

- It builds, the application starts, and any tests the task asked for pass.
- It does only what the change's tasks ask: no extra tests, checks, abstractions, caching or
  hardening.
- It stays inside one module (or is the same narrow change repeated across modules).
- It does not cross a module boundary through the database.
- It commits through Graphite, on a stacked branch, with a message that follows the template above
  and carries the owner's signature and nothing else.

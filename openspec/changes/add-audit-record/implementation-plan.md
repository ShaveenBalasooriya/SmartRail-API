# Implementation plan: add-audit-record

Planner/reviewer: Claude Sonnet 5.5 (orchestrator). Coding is delegated to OpenCode (`opencode-go/deepseek-v4.1-flash`) as seven small work packages (WP1 to WP7), run **one at a time, in order**, in the shared checkout (branch `test`). Concurrent builds in one working tree clash on `obj/`, so no two agents run together. After each package the orchestrator reads the diff, runs `dotnet build SmartRail.sln -warnaserror` and the affected tests, and only then ticks the task in `tasks.md` and starts the next package. Nothing is committed by agents. Specs (`proposal.md`, `design.md`, `specs/`) are not edited by agents; gaps are reported back instead.

## Rules every package follows

1. Read `proposal.md`, `design.md`, `specs/audit-record/spec.md` and your own package below before changing anything. Do not invent requirements; if something is unclear, stop and report it.
2. Match the neighbouring code: look at how `RailNetwork` (queries, endpoints, DbContext, migrations) and `IdentityAccess` (outbox, `AccessDeniedAuthorizationResultHandler`) are built and copy their conventions (namespaces, XML doc style, `ArgumentNullException.ThrowIfNull`, file-scoped namespaces, Carter modules, Result/ToProblemDetails).
3. Dependency rules are tested by `tests/SmartRail.ArchitectureTests`. Domain depends only on SharedKernel; Application has no EF Core or ASP.NET Core; Api has no Infrastructure or EF Core; no module Domain/Application/Contracts references `SmartRail.SharedKernel.Infrastructure`.
4. No new NuGet packages unless the package says so. Versions are central (`Directory.Packages.props`).
5. Migrations: `dotnet ef migrations add <Name> --project src/Modules/Audit/SmartRail.Audit.Infrastructure` (dotnet-ef is installed; `AuditDbContextFactory` exists). Never hand-edit the Designer or snapshot.
6. Verification before you finish: `dotnet build SmartRail.sln -warnaserror` clean, plus the tests your package names. Integration tests need Docker (Testcontainers). Report the exact commands run and their real results. Never claim a test passed that you did not run.
7. Do not tick `tasks.md`; the orchestrator does that after review.
8. **AI log:** before finishing, append one row to `../docs/ai-usage/AI-INTERACTION-LOG.md` (repo root `docs/ai-usage/`), following the `ai-usage-log` skill in `.opencode/skills/ai-usage-log/SKILL.md` at the repository root (the parent of `SmartRail-API`). Use the next number after the highest existing row. Record the model, what you were asked, what you built, how you validated it, and any limitation. Do not fabricate.

## Decisions the spec leaves open (made here; owner to confirm)

- **D-1: the event ID reaches the consumer inside the event.** `IEventConsumer<T>.Handle` receives only the payload, not the outbox message ID, so "idempotent by event ID" (design D5) has nothing to key on. Decision: add `Guid EventId` to the `AccessDenied` contract; the Identity handler generates it once and passes the same value to `Publish(event, eventId)`. The consumer stores it in `audit.entries.source_event_id`. The bus is not changed.
- **D-2: where `IAuditRecorder` lives.** `SmartRail.Audit.Contracts` holds `IAuditRecorder` and the request type; `SmartRail.Audit.Infrastructure` implements it. Audit's own `AuditDbContext` is the context that appends, and the Host's shared request transaction is what makes it the same transaction as the caller's change (D1).
- **D-3: `IRequestTransaction` mechanism.** Scoped, in the Host (`SmartRail.Host/Persistence`). It lazily opens one `NpgsqlConnection` and `NpgsqlTransaction`; each module `DbContext` is pointed at that connection and transaction before its first `SaveChanges` in the request (`Database.SetDbConnection` then `Database.UseTransaction`; EF does not commit a transaction it did not start). A Host endpoint filter on state-changing methods (POST, PUT, PATCH, DELETE) commits once after the handler succeeds and rolls back when it throws or returns a 5xx. GET requests never open one.
- **D-4: page size.** The spec says `pageSize` is at most 100; `PageRequest.MaxPageSize` is 200. The audit list rejects more than 100 with HTTP 400 and does not change the shared type.
- **D-5: no state-changing endpoint exists yet.** Nothing in the running app calls `IAuditRecorder` for a staff change until `add-controller-operations` and `add-departure-logging`. Recorder and transaction are proven through integration tests and the test-only routes in `tests/SmartRail.Host.IntegrationTests/Infrastructure/TestEndpoints.cs`.

## Work packages

### WP1: Audit domain, canonical form and hash (task 1.1)
- New in `SmartRail.Audit.Domain`: `AuditEntry` (all columns in `docs/data-model.md` Audit section, including `ChainPosition`, `PreviousHash`, `EntryHash`, nullable `RelatedEntryId`, nullable `SourceEventId`), `AuditOutcome` (`succeeded`, `denied`), a canonical serialiser and a hash function (design D3: sorted keys, no whitespace, UTC timestamps at microsecond precision, SHA-256, genesis previous hash = 64 zeros). `SourceEventId` is not part of the hash.
- New test project `tests/SmartRail.Audit.UnitTests` (copy the csproj shape of `SmartRail.RailNetwork.UnitTests`; add to `SmartRail.sln`).
- Verify: unit tests that identical content gives an identical hash; changing any one hashed field changes it; genesis uses 64 zeros; timestamps with sub-microsecond ticks hash the same as their microsecond-truncated value; `SourceEventId` changes nothing; `dotnet test tests/SmartRail.Audit.UnitTests` and `tests/SmartRail.ArchitectureTests`.

### WP2: audit.entries mapping, migration, append-only trigger (task 1.2)
- Depends on WP1. `AuditDbContext` maps `AuditEntry` to `audit.entries` (snake_case like the other modules; `before`/`after` as `jsonb`; unique index on `chain_position`; nullable unique index on `source_event_id`; FK `related_entry_id` to `entries.id`).
- Migration `AddAuditEntries`, with the `BEFORE UPDATE OR DELETE` trigger and its function written in the migration's `Up` (raw SQL) and dropped in `Down` (design D4).
- Verify: integration test in `tests/SmartRail.Host.IntegrationTests` (new `Audit/` folder, `[Collection(PostgresCollection.Name)]`) that inserts one row with raw SQL through `PostgresFixture.ExecuteAsync`, then asserts an UPDATE and a DELETE both fail with the trigger's message; `dotnet build -warnaserror`; the existing integration suite still passes (migrations apply on start).

### WP3: IRequestTransaction in the Host (task 1.3)
- Implement D-3. **Layering correction (orchestrator, before WP3 started):** a module's Infrastructure project cannot reference the Host, so `IRequestTransaction` and the enlistment interceptor live in `SmartRail.SharedKernel.Infrastructure` (next to `OutboxSignalInterceptor`), and each module attaches it in its `AddDbContext` lambda exactly like `.AddOutboxSignal(provider)`; the Host owns the endpoint filter that begins and commits it and its registration in `HostComposition.cs`. The design text says "in the Host"; this is the closest placement the dependency rules allow. No module project changes except what enlistment strictly needs; if enlistment needs a hook in a module's `DbContext` registration, use an EF Core interceptor registered by the Host through `ConfigureDbContext`-style options and report it.
- Verify: integration test using test-only routes added to `TestEndpoints.cs`: a POST that writes through two different module DbContexts and then throws leaves **neither** write; the same POST without the throw leaves both; a GET opens no transaction. Existing tests pass.

### WP4: IAuditRecorder with the advisory-locked append (task 2.1)
- Depends on WP1 to WP3. `IAuditRecorder.Record(...)` and its request type in `SmartRail.Audit.Contracts` (actor staff ID, actor role, action, target type/id, before/after JSON, optional related entry ID; outcome `succeeded`). Implementation in `SmartRail.Audit.Infrastructure`: `pg_advisory_xact_lock(<fixed audit key>)`, read last position and hash (genesis when none), compute the hash with the WP1 function, add the entry to `AuditDbContext`; the caller's request transaction commits it (design D1, D2). Register it in `AddAuditInfrastructure`.
- Verify: integration test through a test-only route: two recorded entries get positions 1 and 2, `previous_hash` of the second equals `entry_hash` of the first, the first's previous hash is 64 zeros; a test with ~10 concurrent requests recording one entry each yields positions 1..10 with no gap and a valid chain; a request that records then fails leaves no entry (spec: audit write and change commit together).

### WP5: audit.access-denied consumer (task 2.2)
- Depends on WP4. Implement D-1: `AccessDenied` gains `Guid EventId`; `AccessDeniedAuthorizationResultHandler` generates it once and uses it for both the contract and `Publish`; update `AccessDeniedRecorder` in the test project and any test that builds `AccessDenied`. New `AccessDeniedConsumer : IEventConsumer<AccessDenied>` named `audit.access-denied`, in Audit's Application layer if it needs no EF, else Infrastructure; it writes a `denied` entry in its own transaction (it runs from the dispatcher, outside any request), skips when an entry with that `source_event_id` exists, and relies on the unique index as the backstop. Register with `AddEventConsumer<AccessDenied, AccessDeniedConsumer>()`.
- Verify: integration test: a Guard refused on `TestEndpoints.ControllerWriteRoute` yields exactly one `denied` entry with role `guard` and the action within a few seconds; delivering the same event twice leaves one entry; restart of the host leaves one entry; a consumer that fails once is retried and yields one entry (spec: audit write fails at first). Existing `StaffAuthorizationTests` still pass.

### WP6: list and detail endpoints (tasks 3.1 and 3.2)
- Depends on WP2. Query handlers in `SmartRail.Audit.Application` behind a reader port; reader implementation in `SmartRail.Audit.Infrastructure`; DTOs in `SmartRail.Audit.Contracts`; Carter module in `SmartRail.Audit.Api` mapping `GET /audit/entries` (filters `actorId`, `from`, `to`, `outcome`, `page`, `pageSize`, newest first, D-4, `from` after `to` returns 400) and `GET /audit/entries/{id:guid}` (404 for unknown, includes before/after and `relatedEntryId`). Both `.RequireStaffRole(Roles.Auditor)`; copy the exact pattern `StaffEndpoints` uses. Never a POST/PUT/PATCH/DELETE route.
- Verify: integration tests as `auditor.wasantha` (see `TestTokens`): newest-first ordering, filter by actor and time, filter by outcome, paging, invalid range 400, detail with a related entry, 404, controller and guard tokens get 403, DELETE returns 403 or 405 and removes nothing, no token returns 401. Rows are inserted with the recorder or raw SQL.

### WP7: integrity endpoint and the no-passenger-data check (task 3.3)
- Depends on WP2 and WP6. `GET /audit/integrity` (Auditor only): streams entries by `chain_position`, recomputes each hash and the link to the previous one, stops at the first mismatch, returns `{ "status": "intact", "entriesChecked": n }` or `{ "status": "broken", "firstFailingPosition": p }`. An empty record is `intact` with 0.
- Verify: integration test: intact on an untouched record; after disabling the trigger in the test, editing one entry's `after` with raw SQL and re-enabling it, the result is `broken` at that entry's position; a broken link (previous hash edited) is also caught. Add the spec's "no passenger data" check: entries produced by every recorded action in the suite contain no token, API key or IP address text.
- Then the orchestrator runs the whole suite, `openspec validate add-audit-record --strict`, and ticks tasks.

## Review procedure after each package
Read the diff, `dotnet build SmartRail.sln -warnaserror`, run the package's tests, run the full suite after WP3, WP5 and WP7, `docker compose up --build -d api` after WP2 and WP6 to confirm a clean boot (migrations apply, health ready). Send failures back with the exact error. Tick `tasks.md` only after the verify passes.

## Gaps and contradictions found in the spec (reported, not fixed)
1. **Consumer cannot see the event ID** (D-1). Design D5 says the consumer is idempotent by event ID; the bus does not pass it.
2. **No state-changing endpoint exists** (D-5), so tasks 2.1 and 1.3 cannot be verified "by running the application"; they are verified in the test host.
3. **`pageSize ≤ 100` vs the shared `PageRequest` maximum of 200** (D-4).
4. **"Audit write fails" scenario** (HTTP 500 and the change is not committed) depends on WP3's rollback-on-5xx behaviour and on a state-changing endpoint; only the test-only routes can exercise it today.
5. **Denials recorded before this change** were marked published with no consumer and are never replayed (event-bus plan gap 5). Nothing to do in MVP 1; there are no denials in a real database yet.

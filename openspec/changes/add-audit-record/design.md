# Design: Audit record

## Context

Table shape is fixed by `../docs/data-model.md` (`audit.entries`, one global chain — owner
decision). Identity already publishes `AccessDenied`. Live Operations (later changes) must write
entries atomically with its own changes although it lives in a different schema and DbContext.

## Goals / Non-Goals

**Goals:** atomic audit with the change; tamper evidence that a database-level edit cannot hide;
no module depends on Audit's internals.

**Non-Goals:** exports, anchoring, SoD reports.

## Decisions

### D1. Atomicity through a shared connection and transaction
All module DbContexts use the same database. A scoped `IUnitOfWork` in the Host opens one
`NpgsqlConnection` and transaction per request; each DbContext enlists via
`Database.UseTransaction`. `IAuditRecorder.Record(...)` (in `SmartRail.Audit.Contracts`) adds the
entry in that transaction; commit happens once at the end. *Alternative:* outbox + async audit —
rejected: an audit entry could lag or be lost relative to the change, which AUD-05's assurance
cannot tolerate.

### D2. Chain ordering and concurrency
`chain_position` is assigned inside the transaction after taking `pg_advisory_xact_lock(<audit key>)`,
reading the last position and hash. This serialises audit appends (cheap at MVP 1 volume) and makes
positions gap-free because the lock is held until commit. *Alternative:* a sequence — rejected:
sequences leave gaps on rollback and race on "previous hash".

### D3. Hash
`entry_hash = SHA-256(canonical JSON of {chain_position, occurred_at, actor_staff_id, actor_role,
action, outcome, target_type, target_id, before, after, related_entry_id} + previous_hash)`; genesis
previous hash is 64 zeros. Canonical JSON: sorted keys, no whitespace, UTC timestamps with
microsecond precision (PostgreSQL's), so recomputation is stable.

### D4. Append-only at the database
The migration revokes UPDATE, DELETE and TRUNCATE on `audit.entries` from the application role and
adds a `BEFORE UPDATE OR DELETE` trigger that raises an exception (defence in depth if grants
drift). Migrations run under the same role in MVP 1, so the trigger is the effective guard.

### D5. Denials
`AccessDeniedHandler : INotificationHandler<AccessDenied>` writes the entry in its own transaction
(the denied request has no business transaction).

### D6. Endpoints
`GET /api/v1/audit/entries?actorId&from&to&outcome&page&pageSize` (pageSize ≤ 100),
`GET /api/v1/audit/entries/{id}`, `GET /api/v1/audit/integrity`. All `.RequireStaffRole(Auditor)`.
The integrity check streams entries in position order and stops at the first mismatch.

## Risks / Trade-offs

- [Advisory lock serialises all audited writes] → MVP 1 write volume is tiny (a handful of guards and
  one controller); revisit with per-partition chains if it ever matters.
- [A superuser can still rewrite the table and recompute hashes] → Tamper evidence, not tamper
  proofing; external anchoring is the deferred answer (EP-03 Out).

## Open Questions

None.

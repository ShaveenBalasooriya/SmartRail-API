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
All module DbContexts use the same database. A scoped `IRequestTransaction` [A] in the Host opens one
`NpgsqlConnection` and transaction per request; each DbContext enlists via
`Database.UseTransaction`. `IAuditRecorder.Record(...)` (in `SmartRail.Audit.Contracts`) adds the
entry in that transaction; commit happens once at the end.

**Decision (owner, 2026-10-21): option X now.** A staff state change and its audit entry commit together
through this shared request transaction. This is the **single documented exception** to "cross-module
events are published to the outbox and delivered after commit by the event bus" (`add-event-bus`;
`../docs/architecture.md` §3 "Event delivery"). The owner acknowledged the small coupling it creates
between modules' persistence (every module's `DbContext` enlists in one connection and transaction) and
accepted it for the sake of simplicity, because AUD-05's assurance cannot tolerate an entry that lags or
is lost relative to its change. It does not weaken the bus: the change's outbox row is saved in the same
shared transaction, so it commits with the audit entry and the dispatcher, which reads only committed
rows, never delivers an event for a change whose transaction is still open or rolled back
(`add-event-bus` D7).

**Planned (owner, 2026-10-21): option Y later.** The owner's plan is to send staff state changes through
the event bus too: the change commits with a durable to-audit message in its own module's outbox and an
Audit consumer writes the chain entry shortly after, with no shared transaction. That is not built now. It
will require rewording AUD-05 and the requirement "State change and audit entry commit together" (the
entry would appear a moment after the change), and the shared request transaction would be removed. No
current requirement changes for it. Callers keep Y possible by recording through `IAuditRecorder` only
(`add-event-bus` D11).

*Naming [A]:* every module already has its own `IUnitOfWork` in `Application/Abstractions` (a module's
`DbContext` implements it). The shared Host-level piece is therefore named `IRequestTransaction`, not
`IUnitOfWork`, so the two cannot be confused or collide in the Host. Wherever other changes say "the
request unit of work" they mean this transaction with the module's `IUnitOfWork` enlisted in it.

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
The migration adds a `BEFORE UPDATE OR DELETE` trigger on `audit.entries` that raises an exception.
Migrations and the application run under the same database role in MVP 1, so revoking grants from
that role would not add protection; the trigger is the guard. *Alternative:* a separate migration
role plus revoked grants. Deferred until deployment uses separate roles.

### D5. Denials
Identity publishes `AccessDenied` through the event bus (`add-event-bus` D9). `AccessDeniedConsumer :
IEventConsumer<AccessDenied>`, named `audit.access-denied` [A], writes the entry in Audit's own
transaction (the denied request has no business transaction). Delivery is at least once with retry, so a
denial is not lost if the audit write fails at that moment; the entry appears shortly after the denial
rather than in the same instant, and the consumer is idempotent by event ID: the denial's event ID is
stored on the entry in a nullable unique `source_event_id` column, outside the hash [A].

### D6. Endpoints
`GET /api/v1/audit/entries?actorId&from&to&outcome&page&pageSize` (pageSize ≤ 100),
`GET /api/v1/audit/entries/{id}`, `GET /api/v1/audit/integrity`. All `.RequireStaffRole(Auditor)`.
The integrity check streams entries in position order and stops at the first mismatch.

## Risks / Trade-offs

- [Advisory lock serialises all audited writes] → MVP 1 write volume is tiny (a handful of guards and
  one controller); revisit with per-partition chains if it ever matters.
- [The shared request transaction couples the modules' persistence] → Acknowledged and accepted by the
  owner for simplicity (2026-10-21). Moving staff state changes onto the event bus (option Y, D1) is the
  planned way out, not built in MVP 1.
- [A superuser can still rewrite the table and recompute hashes] → Tamper evidence, not tamper
  proofing; external anchoring is the deferred answer (EP-03 Out).

## Open Questions

None.

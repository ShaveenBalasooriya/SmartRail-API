# Proposal: Audit record

## Why

The Auditor's core pain is that nobody can prove the record wasn't changed, or that roles stayed in
their lanes (`../docs/user-journeys/06-auditor.md`). Every staff state change and every denied attempt
must land in an append-only, tamper-evident record with no passenger data (M-03), and the "Separation
of duties denials" and "Wrong cancellation corrected" end-to-end flows both end in this record.

Delivers **EP-03**. Stories: AUD-01 to AUD-07; the audit half of NC-08, GRD-09.

## What Changes

- An `audit.entries` table, append-only at the database level, with one global hash chain.
- An audit port (contract) other modules call to record a state change inside the same transaction
  as the change itself.
- A handler that records every `AccessDenied` notification from Identity.
- Auditor endpoints: filtered, paginated list; entry detail with the link to a related entry; chain
  integrity check.

## Capabilities

### New Capabilities

- `audit-record`: recording staff state changes and denials in a tamper-evident chain, and the
  Auditor's read-only access to it.

### Modified Capabilities

None.

## Out of Scope

- Verified exports, separation-of-duties reports, external anchoring of the chain (EP-03 Out).
- Recording passenger activity of any kind (M-03).
- Recording denied reads (only denied writes are recorded, per `role-based-access`).
- Retention and purge (EP-14 Out).
- The individual actions that write audit entries — each later change records its own.

## Impact

- New `audit` schema with a trigger that refuses UPDATE and DELETE on its entries.
- `SmartRail.Audit.Contracts` (`IAuditRecorder`), used by Live Operations in later changes.
- Auditor endpoints under `/api/v1/audit`.

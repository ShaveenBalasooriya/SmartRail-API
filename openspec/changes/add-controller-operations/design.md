# Design: Network Controller operations

## Context

Table follows `../docs/data-model.md` (`operations.status_changes`, `corrects_change_id`,
`provenance`). The calculator and fact sink from `add-live-running-state` already treat a change as a
fact and drop corrected ones; Audit's recorder is available from `add-audit-record`.

## Goals / Non-Goals

**Goals:** a write path where validation, state change, event and audit entry succeed or fail
together; corrections that need no special-case logic.

**Non-Goals:** early termination, notices, review gates.

## Decisions

### D1. Endpoints
All `.RequireStaffRole(NetworkController)`:
- `GET /api/v1/operations/journeys` — today's journeys (live view).
- `GET /api/v1/operations/journeys/{journeyId}` — detail with change history.
- `POST /api/v1/operations/journeys/{journeyId}/delays` `{ fromStopSequence, minutes }`
- `POST /api/v1/operations/journeys/{journeyId}/cancellation` `{}`
- `POST /api/v1/operations/journeys/{journeyId}/platform-changes` `{ stopSequence, platformId }`
- `POST /api/v1/operations/changes/{changeId}/corrections` `{}`
Separate resource per type keeps each body and validation small and makes the "Guard tries to
publish" denial a single policy on a route group.

### D2. One write pipeline
Each POST is a MediatR request run inside the request unit of work: load live state with the row
lock → validate against current state (spec rules) → insert `status_changes` row → sink applies the
fact (recompute, event) → audit recorder entry → commit. Any validation failure returns before
anything is written.

### D3. Correction as a fact flag
A correction inserts a `correction` row with `corrects_change_id`. The calculator receives the
original with `corrected = true` and ignores it; the correction itself contributes no values. This
gives "as if never published" for free, including for cancellation → status returns to
`scheduled`/`running` from departures.

### D4. Audit before/after
Before/after are the affected fields only (e.g. `{platform: "2"}` → `{platform: "3"}`; cancellation:
`{status: "scheduled"}` → `{status: "cancelled"}`; correction: the state fields the correction
reverted). The correction's audit entry sets `related_entry_id` to the original change's audit entry,
found by a `status_change_id` column kept in the audit `target_id` of the original entry.

### D5. Stale threshold
`Operations:StaleAfterMinutes`, default 10, evaluated at read time against `TimeProvider`.

## Risks / Trade-offs

- [Two controllers act at once on one journey] → Row lock serialises; second request validates
  against the first's result (e.g. same platform → 409).
- [A mis-keyed delay minutes value] → Correctable; no pre-confirmation by design (W-08, W-09).

## Open Questions

None.

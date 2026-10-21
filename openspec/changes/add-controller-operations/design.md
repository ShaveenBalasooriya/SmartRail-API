# Design: Network Controller operations

## Context

Table follows `../docs/data-model.md` (`operations.status_changes`, `corrects_change_id`,
`provenance`). The calculator and fact sink from `add-live-running-state` already treat a change as a
fact and drop corrected ones; Audit's recorder is available from `add-audit-record`.

## Goals / Non-Goals

**Goals:** a write path where validation, state change, event and audit entry succeed or fail
together; corrections that need no special-case logic.

**Non-Goals:** skipped stops, termination reasons, notices, review gates.

## Decisions

### D1. Endpoints
All `.RequireStaffRole(NetworkController)`:
- `GET /api/v1/operations/journeys` — today's journeys (live view).
- `GET /api/v1/operations/journeys/{journeyId}` — detail with change history.
- `POST /api/v1/operations/journeys/{journeyId}/delays` `{ fromStopSequence, minutes }`
- `POST /api/v1/operations/journeys/{journeyId}/cancellation` `{}`
- `POST /api/v1/operations/journeys/{journeyId}/platform-changes` `{ stopSequence, platformId }`
- `POST /api/v1/operations/journeys/{journeyId}/termination` `{ lastStopSequence }` (D7)
- `POST /api/v1/operations/changes/{changeId}/corrections` `{}`
Separate resource per type keeps each body and validation small and makes the "Guard tries to
publish" denial a single policy on a route group.

### D2. One write pipeline
Each POST is a MediatR request run inside the request transaction (the module `IUnitOfWork` enlisted in the shared `IRequestTransaction`, `add-audit-record` D1 [A]): load live state with the row
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
`{status: "scheduled"}` → `{status: "cancelled"}`; early termination: `{status: "running"}` →
`{status: "terminated", lastStopSequence: 3}`; correction: the state fields the correction
reverted). The correction's audit entry sets `related_entry_id` to the original change's audit entry,
found by a `status_change_id` column kept in the audit `target_id` of the original entry.

### D5. Stale threshold
`Operations:StaleAfterMinutes`, default 10, evaluated at read time against `TimeProvider`.

### D6. Guard display name through an Identity query contract
The live state holds only the guard's staff ID (a plain identifier across the module boundary), but
the live view must show the guard's display name (NC-01, NC-07). Staff names live in the `identity`
schema, so the live view resolves them through a new in-process query contract, `IStaffDirectory`
in `SmartRail.IdentityAccess.Contracts`, implemented in Identity's Application layer by sending the
module's own query (architecture §3, same pattern as `INetworkCatalog`). [A] It takes a set of staff
IDs and returns each ID's display name only, so one call serves the whole list and nothing else
about a staff member crosses the boundary. It is consumed only by the staff-only operations
endpoints; public and provider responses never carry staff names or IDs.

### D7. Early termination (owner decision 2026-10-21)
The owner brought early termination into MVP 1 for one case: a running train that cannot continue
(for example a rolling-stock fault). The specific shape below is [A].
- Endpoint: `POST /api/v1/operations/journeys/{journeyId}/termination` `{ lastStopSequence }`,
  `.RequireStaffRole(NetworkController)`, through the D2 pipeline like the other changes.
- Stored as a `status_changes` row of type `termination` (a fifth `change_type`; `docs/data-model.md`
  is updated), `stop_sequence` being the last stop served, provenance `automatically_published`.
  Nothing else is captured: no reason taxonomy, no free text.
- A `Termination` change kind in the calculator (`add-live-running-state` is built and stays as it
  is; task 2.6 extends it). Once an uncorrected termination exists the status is `terminated`; stops
  after `lastStopSequence` keep scheduled times, report no expected times and the journey reports no
  next station, as for `cancelled`. `terminated` is a new lifecycle status, deliberately not
  `completed`, because the train did not reach its terminus.
- A new event type `termination_published`, announced like the others and pushed to the provider
  (`add-catering-integration`).
- A correction can revert a termination through the existing mechanism (D3): the termination is
  flagged corrected and ignored, so the journey returns to `running` from its departures. No special
  case; this covers a termination made on the wrong journey.
- A terminated journey is closed to further changes like a completed one, and the automatic
  completion job never touches it (`add-departure-logging`).

## Risks / Trade-offs

- [Two controllers act at once on one journey] → Row lock serialises; second request validates
  against the first's result (e.g. same platform → 409).
- [A mis-keyed delay minutes value] → Correctable; no pre-confirmation by design (W-08, W-09).

## Open Questions

None.

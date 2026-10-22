# Proposal: Network Controller operations

## Why

The Network Controller has no single live view, relays changes by hand, and cannot correct a wrong
publish (`../docs/user-journeys/02-network-controller.md`). MVP 1 exists to prove that one change made
here reaches every channel, labelled and audited, and that a mistake can be corrected everywhere
without erasing the original (M-15, M-16, S-12).

Delivers **EP-08**. Stories: NC-01 to NC-08; the publishing half of the "Delay reaches every channel",
"Platform change everywhere" and "Wrong cancellation corrected" end-to-end flows. Depends on
`add-live-running-state`, `add-audit-record` and `add-event-bus` (its events travel the bus).

## What Changes

- Operations endpoints: today's journeys with status, delay, last/next station, freshness and a stale
  flag; one journey with its stops and change history.
- Publishing a delay, a cancellation or a platform change, each immediately effective, labelled
  `automatically_published`, attributed to the controller, audited and added to the event stream.
- Correcting a published change: a new change referencing the original, which stays recorded; the
  journey returns to the state it would have had without the original.
- Publishing an early termination of a running journey that cannot continue (owner decision
  2026-10-21): the journey becomes `terminated`, and the correction mechanism can revert it. See
  `status-publication` and design D7.

## Capabilities

### New Capabilities

- `controller-live-view`: the Network Controller's picture of today's railway.
- `status-publication`: publishing delays, cancellations, early terminations and platform changes.
- `change-correction`: correcting a published change without erasing it.

### Modified Capabilities

None.

## Out of Scope

- Skipped stop changes (deferred). Early termination is limited to ending a running journey at a
  chosen stop; no reason is captured and a scheduled journey is cancelled, not terminated.
- Notices to crew and acknowledgements (deferred with the Driver).
- Human-authored passenger messaging and its trilingual approval (M-21 deferred).
- Incidents, restrictions, handoffs to a Scheduler.
- Pausing or reviewing changes before they publish (W-08, W-09: never built).
- The operations web application itself; live push of the view (`add-live-distribution`).

## Impact

- New `operations.status_changes` table.
- Endpoints under `/api/v1/operations`.

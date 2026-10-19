# Proposal: Network Controller operations

## Why

The Network Controller has no single live view, relays changes by hand, and cannot correct a wrong
publish (`../docs/user-journeys/02-network-controller.md`). MVP 1 exists to prove that one change made
here reaches every channel, labelled and audited, and that a mistake can be corrected everywhere
without erasing the original (M-15, M-16, S-12).

Delivers **EP-08**. Stories: NC-01 to NC-08; the publishing half of the "Delay reaches every channel",
"Platform change everywhere" and "Wrong cancellation corrected" end-to-end flows. Depends on
`add-live-running-state` and `add-audit-record`.

## What Changes

- Operations endpoints: today's journeys with status, delay, last/next station, freshness and a stale
  flag; one journey with its stops and change history.
- Publishing a delay, a cancellation or a platform change, each immediately effective, labelled
  `automatically_published`, attributed to the controller, audited and added to the event stream.
- Correcting a published change: a new change referencing the original, which stays recorded; the
  journey returns to the state it would have had without the original.

## Capabilities

### New Capabilities

- `controller-live-view`: the Network Controller's picture of today's railway.
- `status-publication`: publishing delays, cancellations and platform changes.
- `change-correction`: correcting a published change without erasing it.

### Modified Capabilities

None.

## Out of Scope

- Skipped stop and early termination changes; cancelling a journey that is already running (that
  is early termination) (deferred).
- Notices to crew and acknowledgements (deferred with the Driver).
- Human-authored passenger messaging and its trilingual approval (M-21 deferred).
- Incidents, restrictions, handoffs to a Scheduler.
- Pausing or reviewing changes before they publish (W-08, W-09: never built).
- The operations web application itself; live push of the view (`add-live-distribution`).

## Impact

- New `operations.status_changes` table.
- Endpoints under `/api/v1/operations`.

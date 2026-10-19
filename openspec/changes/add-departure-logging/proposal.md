# Proposal: Guard departure logging

## Why

A departure is confirmed verbally today and never becomes data, so the live picture has to be
guessed (`../docs/user-journeys/03-guard.md`). In MVP 1 the Guard's per-station departure log is the
only source of live progress, and it must survive weak signal: logs queue on the device and must
sync exactly once, with their original times (M-23, M-24).

Delivers the backend of **EP-06**. Stories: GRD-01 to GRD-09 (GRD-06's queue display is a client feature; the server supplies the acknowledgement it needs); the server half of the "Offline guard
logs sync once" end-to-end flow. Depends on `add-live-running-state` (fact sink) and
`add-identity-access` (guard role, current actor).

## What Changes

- Guard endpoints listing the caller's assigned journeys with stops, times, current platforms and
  live status.
- A departure-log endpoint accepting one log per calling stop with a client-generated ID and the
  original device timestamp: idempotent replays, conflict detection, validation, an optional delay
  cause from a fixed list.
- Each accepted log becomes a departure fact in the live state and is audited.

## Capabilities

### New Capabilities

- `guard-assignments`: what a Guard can see of the journeys assigned to them.
- `departure-logging`: recording actual departures, including offline-queued ones, exactly once.

### Modified Capabilities

None.

## Out of Scope

- The Guard mobile app itself, its local queue and its UI (a client application; this change is the
  server contract it syncs against).
- Correcting or deleting a mis-tapped departure (full stop-state model deferred; GRD open point).
- Arrival logging, stop-state model, location verification, safety-event capture (EP-06 Out).
- Live push to the Guard app (`add-live-distribution`).
- Driver view and notices to crew (deferred).

## Impact

- New `operations.departure_logs` table.
- Endpoints under `/api/v1/guard`.

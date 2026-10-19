# Proposal: Live running state and journey event stream

## Why

Passengers, the station display, the Guard, the Network Controller and the catering provider all
need the same answer to "where is this train and how late is it" (M-01, M-07, M-08). That answer has
to be computed once, from the facts — the timetable baseline, the Guard's departure logs and the
Controller's published changes — and every change to it must be recorded once, in order, so every
channel receives the same sequence (EP-01 domain event log, EP-07).

Delivers **EP-07** and the domain event log of **EP-01**. Stories: NC-01 (data), NC-07, PAX-04,
PAX-14, CAT-07 (sequence numbers), GRD-07 (progress from original timestamps).

Depends on `add-rail-network-timetable` (journey announcements) and `add-audit-record` (shared unit of
work). Departure logs arrive with `add-departure-logging`; status changes with
`add-controller-operations` — this change defines how they affect the state.

## What Changes

- Live state per journey created from each journey announcement, with the timetable baseline; plus a
  start-up reconciliation for journeys missing live state.
- One deterministic rule set that computes lifecycle status, actual and expected times, minutes early
  or late, last and next station, current platform per stop and freshness from the facts recorded so
  far — recomputed from scratch whenever a fact is added, so a correction simply removes a fact.
- Automatic completion of running journeys.
- The `journey_events` stream: every change to a journey's state appends one event with a gap-free
  per-journey sequence number and a global stream position, and is announced after commit.
- An in-process query contract for live state used by Distribution, Catering and the operations
  endpoints.

## Capabilities

### New Capabilities

- `live-running-state`: how a journey's live state is derived from its baseline, departures and
  published changes.
- `journey-event-stream`: the ordered, persisted record of every journey state change and its
  numbering.

### Modified Capabilities

None.

## Out of Scope

- The endpoints that record departures (`add-departure-logging`) or publish changes
  (`add-controller-operations`).
- Delivering events to clients (`add-live-distribution`) or the provider (`add-catering-integration`).
- Predictive arrival estimates, recovery of lost time, GPS or crew-device location (EP-07 Out).
- Skipped stops and early termination (deferred).

## Impact

- New `operations` schema tables `journey_live_states`, `journey_stop_live_states`,
  `journey_events`.
- `SmartRail.LiveOperations.Contracts`: `ILiveJourneys` query contract, `JourneyStateChanged`
  notification.

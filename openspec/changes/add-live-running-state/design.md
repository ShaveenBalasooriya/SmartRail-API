# Design: Live running state and journey event stream

## Context

Tables follow `../docs/data-model.md` (`operations.journey_live_states`,
`journey_stop_live_states`, `journey_events`). The facts that drive state (`departure_logs`,
`status_changes`) are added by the next two changes; this change defines the calculation and the
stream so both can plug in without changing it.

## Goals / Non-Goals

**Goals:** one pure, unit-testable calculation; corrections handled by recomputation, not by
special-case undo logic; gap-free ordering.

**Non-Goals:** any write endpoint; delivery to clients.

## Decisions

### D1. Pure calculator over facts
`LiveStateCalculator.Compute(Baseline, IReadOnlyList<Fact>, DateTimeOffset now) → JourneyState`
in the Domain layer. `Fact` is either `DepartureFact(stopSequence, departedAt, recordedAt,
streamOrder)` or `ChangeFact(kind, stopSequence?, delayMinutes?, platformId?, publishedAt,
streamOrder, corrected: bool)`. Corrected facts are filtered out before computing. "Most recent"
means highest `streamOrder` (commit order), not wall-clock, so the rule is deterministic.
*Alternative:* incrementally mutate state per event and write inverse operations for corrections —
rejected: inverse logic for each change type is where correction bugs live; recomputing a journey of
≤ 5 stops is trivially cheap.

### D2. Applying a fact (later changes call this)
> **Superseded by refactor-module-architecture:** `IJourneyFactSink` became the `ApplyJourneyFact`
> command (`ApplyJourneyFactCommand`), which later changes send through MediatR; the behaviour below is
> unchanged. Wherever this change says "fact sink" or "sink", read that command.

`IJourneyFactSink.Apply(journeyId, fact)` (Application layer): in one database transaction it takes
`SELECT ... FOR UPDATE` on the journey's live-state row, loads all facts, recomputes, writes the
state and stop rows, and — only if the computed state differs from the stored one — appends a journey
event with `sequence_number = last_sequence_number + 1`. The row lock serialises facts per journey,
which gives gap-free sequence numbers.

### D2a. Creating live state
A `JourneyGenerated` handler creates the live state and stop rows with `ON CONFLICT DO NOTHING`. At
start-up (after generation) a backfill creates live state for any journey that lacks it, reading the
baseline from `ITimetable`. This repairs a journey whose announcement was lost, so the generator stays
non-transactional.

### D3. Stream position
`stream_position` is a `bigint` from a PostgreSQL sequence, assigned at insert. Sequences can leave
gaps on rollback and can commit out of order under concurrency; consumers therefore treat positions
as ordered but not contiguous, and Distribution reads "after position N". The spec only promises
strict increase, which a sequence gives.

> **Clarified 2026-10-21 (owner decision; `add-event-bus`):** consumers receive `JourneyStateChanged`
> through the event bus, which promises order only per journey, and Distribution's resume skips by event
> ID rather than by position (`add-live-distribution` D4). The residual risk described here stays
> accepted. Nothing changes in this change.

### D4. Completion
The calculator marks a journey `completed` when it is computed after the terminus arrival and the
penultimate calling stop has departed. The job that triggers that recomputation arrives with
`add-departure-logging`, the first change that makes it observable.

### D5. Announcement
After the sink's transaction commits it publishes
`JourneyStateChanged(journeyId, eventId, sequenceNumber, streamPosition, eventType)` through
MediatR. A wider post-commit mechanism is added when a caller needs one.

> **Clarified 2026-10-21 (owner decision; `add-event-bus`, `../docs/architecture.md` §3 "Event delivery"):**
> this announcement moves from a direct MediatR publish after commit to the event bus: the sink saves an
> outbox row in the `operations` schema in its own transaction and the bus delivers `JourneyStateChanged`
> to consumers after commit, at least once, in order per journey. The contract's contents are unchanged.
> The code change is a task in `add-event-bus` (2.2); this change's history is not rewritten.

### D6. Snapshot shape
The event payload snapshot is the same `LiveJourneyView` the contract returns (journey fields + stops
with scheduled/expected/actual times, current platform, platform-changed time), serialised as JSONB.

## Risks / Trade-offs

- [Out-of-order commit of stream positions could, in theory, make a catch-up miss an event] → Needs
  two journeys committing within milliseconds while a client reconnects; accepted at MVP 1 volume
  (one controller, a handful of guards). A catch-up safety window is the fix if it is ever seen;
  the bus's per-journey ordering and Distribution's skip-by-ID resume cover the common case (see the D3 note).
- [Completion depends on the expected arrival; a guard who never logs the penultimate stop leaves the
  journey `running`] → Visible as stale in the operations view; accepted for MVP 1.

## Open Questions

None.

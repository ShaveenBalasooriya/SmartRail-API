# Design: Guard departure logging

## Context

Table follows `../docs/data-model.md` (`operations.departure_logs`, `id` generated on the device,
unique `(journey_id, stop_sequence)`). The fact sink from `add-live-running-state` applies the
departure; Audit records it in the same unit of work.

## Goals / Non-Goals

**Goals:** a sync contract the offline Guard app can retry blindly; clear, distinct status codes so
the app can tell "done", "already done" and "will never succeed".

**Non-Goals:** batch endpoint, corrections of logs.

## Decisions

### D1. Endpoints
- `GET /api/v1/guard/journeys` — today's assigned journeys (summary + calling stops).
- `GET /api/v1/guard/journeys/{journeyId}` — detail; 404 if not assigned.
- `POST /api/v1/guard/departure-logs` — body `{ id, journeyId, stopSequence, departedAt, delayCause? }`.
All `.RequireStaffRole(Guard)`.

### D2. Status codes the app relies on
201 accepted · 200 identical replay · 409 conflict (same ID different content, or stop already
logged) · 400 invalid (never retry) · 403 not assigned · 404 unknown journey. The app clears its
queue item on 200/201/409/400 and keeps it on network errors and 5xx. Documented in the endpoint's
OpenAPI description.

### D3. Idempotency check order
Inside the unit of work: look up by ID first (replay/409), then validate, then insert relying on the
unique `(journey_id, stop_sequence)` constraint to settle the double-tap race (catch the violation →
409). "Identical content" compares journeyId, stopSequence, departedAt (to the microsecond) and
delayCause.

### D4. Single-log endpoint, not batch
The app sends queued logs one at a time in tap order. *Alternative:* batch endpoint — rejected for
MVP 1: partial-success semantics complicate the app for at most a handful of queued items.

## Risks / Trade-offs

- [A mis-tap can't be undone] → Deferred by scope; the Network Controller's delay or correction tools
  only affect published changes, so this is recorded as a known limitation for the owner.
- [Device clock wrong by minutes] → The two-minute future bound and the ordering checks limit damage;
  a slow device clock is still accepted as fact.

## Open Questions

- Should a Controller-side tool correct a mis-tapped departure (GRD open point)? Deferred by the lean
  scope; does not change these specs.

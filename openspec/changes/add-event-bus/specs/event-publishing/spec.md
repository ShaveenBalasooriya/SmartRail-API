# Spec Delta

## Purpose

Gives every module one way to publish an event that other modules react to, written atomically with the change that caused it, so a committed change always has its event and a rolled-back change never does (owner decision 2026-10-21, `../docs/architecture.md` §3 "Event delivery"; EP-01 domain event log).

## ADDED Requirements

### Requirement: One publishing path for cross-module events
Every event that one module publishes for other modules SHALL be published through the integration-event publisher; no module SHALL publish a cross-module event directly with MediatR (owner decision 2026-10-21; EP-01). Commands, queries and notifications handled inside one module are unaffected.

#### Scenario: Journeys generated
- **WHEN** the generator inserts new journeys
- **THEN** a `JourneyGenerated` message is recorded through the publisher and not published directly

#### Scenario: Journey state changed
- **WHEN** a fact changes a journey's live state
- **THEN** a `JourneyStateChanged` message is recorded through the publisher and not published directly

#### Scenario: Access denied
- **WHEN** an endpoint refuses a state-changing request with 403
- **THEN** an `AccessDenied` message is recorded through the publisher and not published directly

### Requirement: Recorded atomically with the change
A published event SHALL be stored as an outbox row in the publishing module's own schema, saved in the same database transaction as the change that caused it, so that a change and its event commit together or not at all (owner decision 2026-10-21, architecture §4: no write crosses a schema boundary) [A].

#### Scenario: Change commits
- **WHEN** a change commits
- **THEN** exactly one outbox row exists for its event, in the publishing module's schema

#### Scenario: Change rolls back
- **WHEN** a change fails after its event was recorded and the transaction rolls back
- **THEN** no outbox row exists and nothing is delivered

#### Scenario: Shared request transaction
- **WHEN** a request that also records an audit entry commits through the shared request transaction
- **THEN** the change, the audit entry and the outbox row commit together

### Requirement: Event identity and ordering key
Each published event SHALL carry an event ID that is unique and stable across redelivery, its type, its occurrence time and an optional ordering key; journey events SHALL use the journey ID as their ordering key (EP-01, CAT-07) [A].

#### Scenario: Journey event
- **WHEN** `JourneyStateChanged` is published for a journey
- **THEN** its event ID is the journey event's ID and its ordering key is the journey ID

### Requirement: Events carry no personal data
A published event SHALL NOT contain passenger personal data, a token, an API key or a client IP address (EP-14).

#### Scenario: Denial event content
- **WHEN** an `AccessDenied` message is inspected
- **THEN** it holds the staff ID when known, the role when known, the attempted action and the time, and nothing else about the caller

# Spec Delta

## Purpose

Delivers every published event to every consumer that registered for it, at least once, in order per journey, without one consumer's trouble affecting another, over a transport that can be replaced later without touching publishers or consumers (owner decision 2026-10-21, `../docs/architecture.md` §3 "Event delivery"; EP-01, CAT-07).

## ADDED Requirements

### Requirement: Every registered consumer receives every event
A dispatcher SHALL hand each recorded event to every consumer registered for its type, after the transaction that recorded it has committed, and SHALL keep delivering until each consumer has handled it or its delivery has failed (owner decision 2026-10-21; EP-01) [A].

#### Scenario: Two consumers
- **WHEN** an event is recorded and two consumers are registered for its type
- **THEN** each consumer handles the event

#### Scenario: Not before commit
- **WHEN** a change has not yet committed
- **THEN** no consumer receives its event

### Requirement: Prompt delivery with a polling fallback
The dispatcher SHALL be woken in-process when a module saves an event and SHALL also poll for undelivered events every second, so a missed wake-up delays delivery by at most the polling interval (EP-09) [A].

#### Scenario: Normal delivery
- **WHEN** an event is committed
- **THEN** its consumers receive it within two seconds

#### Scenario: Missed wake-up
- **WHEN** the wake-up is lost
- **THEN** the event is still delivered by the next poll

### Requirement: At-least-once delivery that survives a restart
Delivery SHALL be at least once: an event whose delivery has not been completed when the application stops SHALL be delivered after it starts again (owner decision 2026-10-21).

#### Scenario: Restart before delivery
- **WHEN** the application restarts with an event recorded but not yet handled by a consumer
- **THEN** the consumer handles it after start-up

### Requirement: Consumers are idempotent by event ID
A consumer SHALL produce the same effect however many times it receives the same event ID, because delivery is at least once (owner decision 2026-10-21; CAT-06).

#### Scenario: Delivered twice
- **WHEN** a consumer receives the same event twice
- **THEN** its stored state is as if it had received it once

### Requirement: Per-consumer delivery state
Each consumer SHALL have its own delivery state for each event, so a consumer that is failing SHALL NOT block, delay or cause repeated delivery to another consumer of the same event [A].

#### Scenario: One consumer failing
- **WHEN** one of two consumers keeps failing on an event
- **THEN** the other consumer handles it once and is not re-sent it

### Requirement: Order per ordering key
For one consumer and one ordering key, events SHALL be delivered in the order they were recorded, and a later event SHALL NOT be delivered while an earlier event for the same key is still being retried; a failed delivery SHALL release the key. No order is promised between different keys (CAT-07) [A].

#### Scenario: Earlier event retrying
- **WHEN** event 4 of a journey is awaiting retry for a consumer and event 5 of the same journey is recorded
- **THEN** that consumer receives event 5 only after event 4 is delivered or failed

#### Scenario: Other journeys unaffected
- **WHEN** one journey's event is retrying
- **THEN** that consumer still receives events for other journeys

### Requirement: Retry with backoff and a failed state
A delivery whose consumer throws SHALL be retried with increasing delays (2 s, 10 s, 1 min, 5 min) and marked `failed` after the fifth failed attempt; a `failed` delivery SHALL NOT be retried again (EP-17) [A].

#### Scenario: Consumer recovers
- **WHEN** a consumer fails once and succeeds on the next attempt
- **THEN** the delivery is marked delivered with two attempts

#### Scenario: Retry limit
- **WHEN** all five attempts fail
- **THEN** the delivery is marked `failed` and no further attempt is made

### Requirement: Transport behind an abstraction
Publishers and consumers SHALL depend only on the publisher and consumer abstractions, and delivery SHALL go through a transport abstraction whose MVP 1 implementation is in-process on PostgreSQL, so that a NATS JetStream transport can replace it without changing any publisher or consumer (owner decision 2026-10-21) [A].

#### Scenario: Dependency direction
- **WHEN** the module projects' references are inspected
- **THEN** no module Domain, Application or Contracts project references the transport or the dispatcher

### Requirement: Failure visibility
SmartRail SHALL count failed delivery attempts and `failed` deliveries as metrics labelled only by event type and consumer name, and log each failed attempt with the event ID, consumer name, attempt number and exception type, without the payload (EP-17, EP-14) [A].

#### Scenario: Failed attempt
- **WHEN** a consumer throws on an attempt
- **THEN** the failure counter increases with the event type and consumer name only, and the log entry carries no payload

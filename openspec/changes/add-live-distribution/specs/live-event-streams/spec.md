# Spec Delta

## Purpose

Delivers every journey state change to connected clients as it happens, and lets a client that was disconnected catch up without missing or duplicating anything (M-01, EP-09).

## ADDED Requirements

### Requirement: Public event stream
The system SHALL provide an anonymous Server-Sent Events endpoint that sends one event per journey state change, optionally filtered to one station or one journey, each carrying the event ID (the stream position), the event type and the journey's resulting live view (EP-09).

#### Scenario: Delay published
- **WHEN** a client is connected and a delay is published on a journey
- **THEN** the client receives a `delay_published` event with the updated live view within two seconds

#### Scenario: Station filter
- **WHEN** a client subscribes to one station
- **THEN** it receives only events for journeys calling at that station

### Requirement: Guard stream scoped to own journeys
The system SHALL provide a Guard-only stream that sends only events for journeys assigned to the calling guard; other roles SHALL receive HTTP 403 (GRD-08, M-04).

#### Scenario: Another guard's journey
- **WHEN** an event occurs on a journey assigned to guard B
- **THEN** guard A's stream does not carry it

### Requirement: Resume from last event
A client reconnecting with `Last-Event-ID` SHALL first receive every event with a higher stream position, in order, and then continue live, with no gap and no duplicate at the join (EP-09, M-02).

#### Scenario: Short drop
- **WHEN** a client misses three events while disconnected and reconnects with its last event ID
- **THEN** it receives those three events in order, then new events as they happen

#### Scenario: Overlap at the join
- **WHEN** an event is committed during catch-up
- **THEN** the client receives it exactly once

### Requirement: Catch-up beyond the retained window
When the requested event ID is older than the oldest retained event, or unknown, the stream SHALL start with a `reset` event telling the client to reload current state from the read endpoints, then continue live (EP-09).

#### Scenario: Stale client
- **WHEN** a client reconnects with an event ID older than the retained window
- **THEN** its first event is `reset`

### Requirement: Heartbeat
An open stream SHALL send a comment heartbeat at least every 20 seconds so clients and proxies can detect a dead connection.

#### Scenario: Quiet network
- **WHEN** no journey changes for a minute
- **THEN** the client still receives heartbeats

### Requirement: No personal data in streams
Streams SHALL carry railway data only; the Guard stream SHALL identify the guard only by the credential used to connect and SHALL NOT log it beyond the staff ID (EP-14, M-03).

#### Scenario: Public stream content
- **WHEN** the public stream's payload is inspected
- **THEN** it holds no staff names, IDs or tokens

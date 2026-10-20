# Spec Delta

## Purpose

Records every change to a journey's live state exactly once, in order, so that every channel — passenger app, display, Guard app, operations view and catering provider — can receive the same sequence and resume from where it left off (EP-01 domain event log, EP-09, CAT-07).

## ADDED Requirements

### Requirement: One event per state change
Every recorded fact that changes a journey's live state, and every automatic completion, SHALL append exactly one journey event in the same transaction as the state change. A fact that changes nothing (such as a replayed departure) SHALL append no event (EP-01).

#### Scenario: Departure recorded
- **WHEN** a new departure is recorded for a journey
- **THEN** one event of type `departure_logged` is appended

#### Scenario: Replay
- **WHEN** an identical departure record is received a second time
- **THEN** no event is appended

### Requirement: Event types
Journey events SHALL have one of the types `departure_logged`, `delay_published`, `cancellation_published`, `platform_changed`, `correction_published` or `journey_completed` (M-16 stripped to three change types, S-12).

#### Scenario: Unknown type
- **WHEN** code attempts to append an event of any other type
- **THEN** the append is refused

### Requirement: Per-journey sequence numbers
Each journey's events SHALL be numbered 1, 2, 3 and so on with no gaps and no repeats, in the order their state changes were committed (CAT-07).

#### Scenario: Concurrent facts on one journey
- **WHEN** a departure and a platform change for the same journey are committed at the same moment
- **THEN** they receive consecutive, distinct sequence numbers

### Requirement: Global stream position
Every event SHALL also carry a stream position that strictly increases across all journeys in commit order, so a client can ask for every event after the last one it saw (EP-09).

#### Scenario: Events on two journeys
- **WHEN** events are appended for journey A and then journey B
- **THEN** B's event has a higher stream position than A's

### Requirement: Event content
Each event SHALL carry its ID, type, journey ID, sequence number, stream position, the fact's details, the source departure or change ID, the time the fact occurred, the time it was recorded and a snapshot of the journey's resulting live state (EP-09, CAT-05).

#### Scenario: Snapshot matches state
- **WHEN** an event is read back
- **THEN** its snapshot equals the journey's live state immediately after that event was committed

### Requirement: Announced only after commit
Each event SHALL be announced to other modules only after the transaction that appended it commits, and SHALL never be announced for a rolled-back change (architecture §3).

#### Scenario: Rolled back
- **WHEN** applying a fact fails after its event was appended but before commit
- **THEN** no announcement is made and the event does not exist

### Requirement: Events readable by position
Other modules SHALL be able to read, through the live-state contract, the events after a given stream position in order, optionally filtered by journey, and the oldest position still retained (EP-09).

#### Scenario: Catch-up read
- **WHEN** a module asks for events after position 120
- **THEN** it receives every event with a position above 120, in position order

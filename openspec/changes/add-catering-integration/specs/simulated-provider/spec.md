# Spec Delta

## Purpose

Provides a small separate application standing in for the external catering provider, so the integration can be demonstrated and tested end to end exactly as a real provider would use it (brief §11, §23, CAT-12).

## ADDED Requirements

### Requirement: Separate process
The simulated provider SHALL run as its own application with its own storage, communicating with SmartRail only through the provider API and its webhook endpoint, and SHALL NOT reference SmartRail's code or database (brief §12: the provider remains a separate system).

#### Scenario: Independence
- **WHEN** the simulated provider is built
- **THEN** it has no project reference to any SmartRail module

### Requirement: Registers and loads state
On start-up the simulated provider SHALL register its webhook URL using its API key and load today's journeys for its station from the provider API (CAT-04, CAT-03).

#### Scenario: Start-up
- **WHEN** the simulated provider starts
- **THEN** its kitchen screen lists today's journeys calling at its station in arrival order

### Requirement: Applies events by event ID and sequence
The simulated provider SHALL ignore an event whose ID it has already processed, and SHALL NOT apply an event whose sequence number is not higher than the last one applied for that journey, while still recording that it received it (CAT-06, CAT-07).

#### Scenario: Duplicate
- **WHEN** the same event is delivered twice
- **THEN** state changes once and the second is shown as ignored duplicate

#### Scenario: Out of order
- **WHEN** event 6 is applied and event 5 then arrives
- **THEN** event 5 is recorded as received but not applied

### Requirement: Cancellation and correction
A cancellation event SHALL mark the journey cancelled on the kitchen screen, and a correction referencing it SHALL restore the journey to the state computed from the remaining applied events (CAT-09).

#### Scenario: Wrong cancellation corrected
- **WHEN** a cancellation and then its correction arrive
- **THEN** the journey shows as running normally again

### Requirement: Kitchen screen
The simulated provider SHALL show its journeys with expected arrival, platform and status, and a log of received events with whether each was applied, ignored as duplicate or recorded as out of order, updating without a page reload (CAT-12).

#### Scenario: Delay arrives
- **WHEN** a delay event is delivered
- **THEN** the journey's expected arrival changes on the screen and the event appears in the log as applied

### Requirement: Failure injection
The simulated provider SHALL have a switch that makes its webhook endpoint answer with an error, so retries can be demonstrated and tested (CAT-08).

#### Scenario: Provider outage
- **WHEN** the switch is on and an event is pushed
- **THEN** the endpoint answers 503 and SmartRail retries

# Spec Delta

## Purpose

Gives each Guard one place to see the journeys they are assigned to, with the stops, times and platforms they work against (GRD-01, GRD-08).

## ADDED Requirements

### Requirement: Own assigned journeys only
A Guard SHALL be able to list today's journeys assigned to them, ordered by scheduled departure, each with lifecycle status, delay, last-updated time and its calling stops; journeys assigned to other guards SHALL NOT appear (GRD-01).

#### Scenario: Two guards
- **WHEN** guard A requests their journeys
- **THEN** only journeys whose assigned guard is A are returned

#### Scenario: No assignments
- **WHEN** a guard with no journey today requests their journeys
- **THEN** an empty list is returned with HTTP 200

### Requirement: Journey detail for the assigned guard
A Guard SHALL be able to read one assigned journey with every calling stop's scheduled and expected times, actual departure if logged, current platform, platform-change time and whether its departure has been logged. Another guard's journey SHALL return HTTP 404.

#### Scenario: Own journey
- **WHEN** the assigned guard opens a journey
- **THEN** each calling stop shows whether its departure is already logged

#### Scenario: Someone else's journey
- **WHEN** a guard requests a journey assigned to another guard
- **THEN** the response is HTTP 404

### Requirement: Changes visible to the guard
The guard's view of a journey SHALL reflect every published delay, cancellation and platform change for it, with the time it was published (GRD-08).

#### Scenario: Platform changed by the controller
- **WHEN** the controller changes the platform at a later stop
- **THEN** the guard's journey detail shows the new platform and when it changed

### Requirement: Guard-only endpoints
Guard endpoints SHALL be callable only with the `guard` role; other roles SHALL receive HTTP 403 (GRD-09, M-04).

#### Scenario: Controller calls a guard endpoint
- **WHEN** a Network Controller token lists guard journeys
- **THEN** the response is HTTP 403

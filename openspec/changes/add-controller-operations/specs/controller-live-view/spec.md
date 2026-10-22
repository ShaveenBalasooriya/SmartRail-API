# Spec Delta

## Purpose

Gives the Network Controller one live view of today's railway, so the picture is no longer assembled by hand from radio and phone, and stale information is visible as stale (NC-01, NC-02, M-15).

## ADDED Requirements

### Requirement: Today's journeys
A Network Controller SHALL be able to list all of today's journeys ordered by scheduled departure, each with line, direction, lifecycle status, minutes early or late, last departed station, next station, assigned guard's display name and last-updated time (NC-01, NC-07).

#### Scenario: Start of shift
- **WHEN** the controller requests today's journeys
- **THEN** every journey dated today is returned with those fields

#### Scenario: Guard shown by name
- **WHEN** a journey has an assigned guard
- **THEN** the journey carries that guard's display name, resolved through Identity's staff directory contract, and not the staff ID

### Requirement: Stale journeys flagged
A running journey whose last-updated time is older than a configured threshold (default 10 minutes) SHALL be returned with `isStale: true` and its last-updated time; scheduled, cancelled, completed and terminated journeys SHALL NOT be flagged stale (NC-02).

#### Scenario: Guard has not logged for a while
- **WHEN** a running journey was last updated 15 minutes ago
- **THEN** it is flagged stale

#### Scenario: Just updated
- **WHEN** a running journey was updated 2 minutes ago
- **THEN** it is not flagged stale

### Requirement: Journey detail with change history
A Network Controller SHALL be able to read one journey with every stop's scheduled, expected and actual times and current platform, and the list of changes published on it, each with type, values, publisher, publish time, provenance and whether it has been corrected (NC-01, NC-06).

#### Scenario: Corrected change in history
- **WHEN** a journey's cancellation was later corrected
- **THEN** its history shows the cancellation marked corrected and the correction that references it

### Requirement: Controller-only view
Operations endpoints SHALL be callable only with the `network_controller` role; Guard and Auditor tokens SHALL receive HTTP 403 (NC-08, M-04).

#### Scenario: Guard opens the live view
- **WHEN** a Guard token requests today's journeys from the operations endpoint
- **THEN** the response is HTTP 403

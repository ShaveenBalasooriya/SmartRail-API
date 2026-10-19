# Spec Delta

## Purpose

Holds the planned train services that run on the network — their direction, stopping pattern, scheduled times, platforms and assigned guard — as validated seed data, because MVP 1 has no Scheduler (mvp-1-scope.md Owner decisions, EP-04).

## ADDED Requirements

### Requirement: Service definition
Each service SHALL belong to one line and one direction (`outbound` follows the line's station order, `inbound` the reverse) and SHALL have a stop for every station of that line, in the direction's order, each marked calling or not calling (EP-04, data-model.md).

#### Scenario: Stop missing from the pattern
- **WHEN** a seeded service omits one of its line's stations
- **THEN** seeding fails naming the service

#### Scenario: Stops out of direction order
- **WHEN** a seeded inbound service lists its stops in outbound order
- **THEN** seeding fails naming the service

### Requirement: Calling pattern rules
A service SHALL call at its first and last station and at least two stations in total; non-calling stations SHALL carry no times and no platform (PAX-03, SCH-03 carried).

#### Scenario: Last station not calling
- **WHEN** a seeded service marks its final station as not calling
- **THEN** seeding fails

#### Scenario: Skipped station has a time
- **WHEN** a non-calling stop has a scheduled time
- **THEN** seeding fails

### Requirement: Scheduled times
Each calling stop SHALL have a scheduled departure, except the last, and a scheduled arrival, except the first. Times SHALL be local times of day in the railway's configured time zone and SHALL strictly increase along the service; a service whose times cross midnight SHALL be rejected (EP-04, owner decision).

#### Scenario: Non-increasing times
- **WHEN** a stop's scheduled arrival is earlier than the previous stop's departure
- **THEN** seeding fails naming the service and the stop

#### Scenario: Arrival after departure at the same stop
- **WHEN** a stop's scheduled arrival is later than its scheduled departure
- **THEN** seeding fails

#### Scenario: Service crosses midnight
- **WHEN** a service departs at 23:40 and arrives at 00:20
- **THEN** seeding fails

### Requirement: Platform per calling stop
Each calling stop SHALL have a platform, and the platform SHALL belong to that stop's station (SCH-06 carried, PAX-02, CAT-03).

#### Scenario: Platform of another station
- **WHEN** a calling stop references a platform belonging to a different station
- **THEN** seeding fails

### Requirement: Assigned guard
Each service SHALL name one assigned guard, and that guard SHALL be a seeded staff account with the `guard` role (GRD-01, mvp-1-scope.md EP-04).

#### Scenario: Guard is not a guard
- **WHEN** a service names a staff account whose role is `network_controller`
- **THEN** seeding fails

### Requirement: Every service runs every day
Every seeded service SHALL run on every calendar day; there SHALL be no running-day calendar in MVP 1 (owner decision, EP-04 Out).

#### Scenario: Weekend
- **WHEN** journeys are generated for a Sunday
- **THEN** every service has a journey that day

### Requirement: Timetable available to other modules
The timetable SHALL be readable by other modules through an in-process query contract that returns a service's line, direction, stops, times, platforms and guard (architecture §3).

#### Scenario: Live Operations builds a journey baseline
- **WHEN** Live Operations asks for the stops of a service through the contract
- **THEN** it receives every stop in order with calling flag, scheduled times and platform

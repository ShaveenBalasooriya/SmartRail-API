# Spec Delta

## Purpose

Holds the planned train services that run on the network: their direction, stopping pattern, scheduled times, platforms and assigned guard (EP-04).

## ADDED Requirements

### Requirement: Service definition
Each service SHALL belong to one line and one direction (`outbound` follows the line's station order, `inbound` the reverse) and SHALL have one stop per station of that line in the direction's order, each marked calling or not calling (EP-04, data-model.md).

#### Scenario: Stored service
- **WHEN** a service with its stops is stored
- **THEN** it can be read back with its line, direction, guard staff ID and every stop in order

### Requirement: Stop times and platforms
A calling stop SHALL have a platform of that stop's station, a scheduled arrival (except the first stop) and a scheduled departure (except the last), as local times of day in the railway's time zone. A non-calling stop SHALL have no times and no platform (EP-04, data-model.md).

#### Scenario: Skipped station
- **WHEN** a service does not call at a station
- **THEN** that stop is stored with no times and no platform

### Requirement: Every service runs every day
Every service SHALL run on every calendar day; there SHALL be no running-day calendar (owner decision, EP-04 Out).

#### Scenario: Weekend
- **WHEN** journeys are generated for a Sunday
- **THEN** every service has a journey that day

### Requirement: Timetable available to other modules
The timetable SHALL be readable by other modules through an in-process query contract that returns a service's line, direction, stops, times, platforms and guard (architecture §3).

#### Scenario: Live Operations builds a journey baseline
- **WHEN** Live Operations asks for the stops of a service through the contract
- **THEN** it receives every stop in order with calling flag, scheduled times and platform

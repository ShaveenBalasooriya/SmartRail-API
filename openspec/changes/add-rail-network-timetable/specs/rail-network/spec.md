# Spec Delta

## Purpose

Describes the physical railway SmartRail runs on: lines, stations, numbered platforms, the order of stations along each line and the interchange where lines meet (EP-04).

## ADDED Requirements

### Requirement: Network structure
The system SHALL store lines, stations and platforms. Line codes and station codes SHALL be unique, and a platform number SHALL be unique within its station (EP-04).

#### Scenario: Duplicate platform number
- **WHEN** a second platform with number `2` is stored for the same station
- **THEN** the database rejects it

### Requirement: Station order per line
Each line SHALL have an ordered sequence of distinct stations, a station SHALL appear at most once on a line, and the inbound direction SHALL use the same stations in reverse order (EP-04, data-model.md).

#### Scenario: Reading a line inbound
- **WHEN** a client reads the stations of a line in the inbound direction
- **THEN** they are returned in reverse sequence order

### Requirement: Interchange derived from line membership
A station SHALL be reported as an interchange when it appears on two or more lines; there SHALL be no separately stored interchange flag (EP-04, PAX-07, data-model.md).

#### Scenario: Shared station
- **WHEN** a station is on two lines
- **THEN** its public representation has `isInterchange: true` and lists both lines

### Requirement: Network read endpoints and contract
The system SHALL provide endpoints that list lines with their ordered stations, list stations with their platforms and lines, and return one station by ID; an unknown station ID SHALL return HTTP 404 problem details. The same data SHALL be available to other modules through an in-process query contract, without those modules reading the `network` schema (EP-04, architecture §3).

#### Scenario: List lines
- **WHEN** a client requests the list of lines
- **THEN** each line is returned with its stations in outbound order, or an empty list when none exist

#### Scenario: Unknown station
- **WHEN** a client requests a station ID that does not exist
- **THEN** the response is HTTP 404 problem details

# Spec Delta

## Purpose

Describes the physical railway SmartRail runs on: lines, stations, numbered platforms, the order of stations along each line and the interchange where lines meet (EP-04).

## ADDED Requirements

### Requirement: Seeded network
Lines, stations, platforms and each line's station order SHALL be loaded from seed data. Station codes and line codes SHALL be unique, and each station SHALL have at least one platform with a number unique within that station (EP-04).

#### Scenario: Duplicate platform number
- **WHEN** a seed file gives one station two platforms numbered `2`
- **THEN** seeding fails and the application does not start

#### Scenario: Station without platforms
- **WHEN** a seed file defines a station with no platforms
- **THEN** seeding fails and the application does not start

### Requirement: Station order per line
Each line SHALL have an ordered sequence of at least two distinct stations, a station SHALL appear at most once on a line, and the reverse direction SHALL use the same stations in reverse order (EP-04, data-model.md).

#### Scenario: Station listed twice on a line
- **WHEN** a seed file lists the same station twice on one line
- **THEN** seeding fails

#### Scenario: Reading a line inbound
- **WHEN** a client reads the stations of a line in the inbound direction
- **THEN** they are returned in reverse sequence order

### Requirement: Interchange derived from line membership
A station SHALL be reported as an interchange when it appears on two or more lines; there SHALL be no separately stored interchange flag (EP-04, PAX-07, data-model.md).

#### Scenario: Shared station
- **WHEN** a station is on both seeded lines
- **THEN** the station's public representation has `isInterchange: true` and lists both lines

### Requirement: Public network endpoints
The system SHALL provide anonymous endpoints that list lines with their ordered stations, list stations with their platforms and lines, and return one station by ID; an unknown station ID SHALL return HTTP 404 (EP-04, PAX-07, CAT-02).

#### Scenario: List lines
- **WHEN** an anonymous client requests the list of lines
- **THEN** both lines are returned, each with its stations in outbound order

#### Scenario: Unknown station
- **WHEN** a client requests a station ID that does not exist
- **THEN** the response is HTTP 404 problem details

### Requirement: Network data available to other modules
The network SHALL be readable by other modules through an in-process query contract returning stations, platforms, line order and interchanges, without those modules reading the `network` schema (architecture §3).

#### Scenario: Distribution needs a station name
- **WHEN** another module asks for a station by ID through the contract
- **THEN** it receives the station's code, name, platforms and lines

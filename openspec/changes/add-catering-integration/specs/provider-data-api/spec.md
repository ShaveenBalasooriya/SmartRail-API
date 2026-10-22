# Spec Delta

## Purpose

Lets the catering provider pull the railway data it needs to time food preparation, and nothing more, through a narrow, key-authenticated, rate-limited, read-only API (M-26, EP-12, CAT-02, CAT-03).

## ADDED Requirements

### Requirement: Stations and platforms
A provider SHALL be able to list stations with their platforms and lines and read one station by ID; an unknown ID SHALL return HTTP 404 (CAT-02).

#### Scenario: List stations
- **WHEN** the provider requests the station list
- **THEN** all nine stations are returned, each with its platforms

### Requirement: Journeys calling at a station
A provider SHALL be able to list journeys calling at a station on a given date (today or tomorrow; default today), each with line, direction, final destination, scheduled and expected arrival at that station, platform at that station, lifecycle status, the sequence number of the latest applied event and last-updated time (CAT-03).

#### Scenario: Today's arrivals
- **WHEN** the provider requests journeys for a station on today's date
- **THEN** every journey calling there is returned in expected-arrival order with the listed fields

#### Scenario: Terminating journey
- **WHEN** a journey ends at the requested station
- **THEN** it is included with its arrival and no departure

#### Scenario: Date out of range
- **WHEN** the date is more than one day ahead
- **THEN** the response is HTTP 400

### Requirement: Read-only and narrow
Provider endpoints SHALL be limited to the listed reads and webhook management; no write, staff or passenger endpoint SHALL accept a provider key (CAT-01, M-26).

#### Scenario: Provider key on staff endpoint
- **WHEN** the provider's key is sent to a staff endpoint
- **THEN** the response is HTTP 401

### Requirement: Railway data only
Provider responses SHALL contain operational railway data only; they SHALL NOT include staff names or IDs, guard assignments, audit data or any passenger data (CAT-10, EP-14).

#### Scenario: Response inspection
- **WHEN** any provider response is inspected
- **THEN** it contains no staff or passenger field

### Requirement: Rate limited
Provider endpoints SHALL be subject to the per-key rate limit and refused with HTTP 429 over it (CAT-11).

#### Scenario: Over the limit
- **WHEN** the provider exceeds the per-minute limit
- **THEN** the response is HTTP 429 with `Retry-After`

### Requirement: Only active providers are served
A valid API key SHALL be refused with HTTP 401 when the provider it was issued to is marked inactive (CAT-01).

#### Scenario: Inactive provider
- **WHEN** a valid key belongs to a provider marked inactive
- **THEN** the response is HTTP 401

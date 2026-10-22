# Spec Delta

## Purpose

Feeds the station platform display with exactly the same live data as the passenger app, shaped for a large screen that shows one platform's next departures (M-14, EP-11, PAX-13 to PAX-16).

## ADDED Requirements

### Requirement: Platform departures feed
The system SHALL provide an anonymous endpoint returning the next departures for one platform of one station (default eight, maximum twenty), each with destination, scheduled and expected departure, minutes early or late, status, platform-change flag and time, guidance text and last-updated time (PAX-13).

#### Scenario: Busy platform
- **WHEN** the display requests platform 2 of a station
- **THEN** the next departures whose current platform is 2 are returned in expected-departure order

#### Scenario: Unknown platform
- **WHEN** the station has no platform with that number
- **THEN** the response is HTTP 404

### Requirement: Current platform decides placement
A journey SHALL appear on the platform it is currently assigned to; after a platform change it SHALL leave the old platform's feed and appear on the new one's (M-09, PAX-15).

#### Scenario: Re-platformed train
- **WHEN** a departure moves from platform 2 to platform 3
- **THEN** platform 3's feed lists it flagged as changed with the time it became known, and platform 2's feed lists a notice that it has moved to platform 3 until its scheduled departure has passed

### Requirement: Same data as the passenger app
For every journey, the display feed SHALL return the same times, status, platform and freshness as the passenger departures endpoint at the same moment (M-14, PAX-14).

#### Scenario: Same journey through both endpoints
- **WHEN** the same journey is read through both endpoints after a delay is published
- **THEN** the times, status and `lastUpdatedAt` are identical

### Requirement: Feed supports staleness handling
The feed SHALL include `generatedAt` and a recommended refresh interval, and SHALL remain readable with the last response cached by the display so it can show last-known data marked stale when the connection drops (M-02, PAX-16).

#### Scenario: Cache headers
- **WHEN** the display receives a feed response
- **THEN** it carries `generatedAt` and a `Cache-Control: no-store` header so the display's own cache, not an intermediary's, decides what is shown when offline

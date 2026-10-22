# Spec Delta

## Purpose

Answers a passenger's core questions without an account: which train, when, how late, from which platform, where is it now and where does it stop (M-07, M-08, M-10, M-12, PAX-01 to PAX-04, PAX-07).

## ADDED Requirements

### Requirement: Departures between two stations
The system SHALL provide an anonymous endpoint listing upcoming journeys that call at an origin station and later at a destination station, each with scheduled and expected departure from the origin, minutes early or late, platform at the origin, lifecycle status, platform-change time, provenance and last-updated time, ordered by expected departure (PAX-02, M-07).

#### Scenario: Direct trains
- **WHEN** a client asks for departures from station A to station B on the same line
- **THEN** only journeys calling at A and then at B are returned, with their origin times and platform

#### Scenario: Skipped station
- **WHEN** a journey passes the destination without calling
- **THEN** it is not listed

#### Scenario: Wrong direction
- **WHEN** the destination comes before the origin in a journey's direction
- **THEN** that journey is not listed

### Requirement: Departures across the interchange are not planned
A request for stations on different lines SHALL return an empty list for direct journeys and SHALL NOT compute a change of line; onward travel is found through departures from the interchange station (mvp-1-scope.md: journey planner deferred, M-12).

#### Scenario: Different lines
- **WHEN** the origin is on line 1 only and the destination on line 2 only
- **THEN** the list is empty and HTTP 200

### Requirement: Departures from a station
The system SHALL provide an anonymous endpoint listing upcoming journeys that depart from a station in the next window (default two hours, maximum six), including the interchange station's onward departures on both lines, each with destination (final calling stop), times, platform, status and freshness (PAX-07, M-12).

#### Scenario: Interchange departures
- **WHEN** a client asks for departures from the interchange station
- **THEN** departures of both lines appear, each naming its line and destination

#### Scenario: Window too large
- **WHEN** the requested window is twelve hours
- **THEN** the response is HTTP 400

### Requirement: What counts as upcoming
A journey SHALL be listed as upcoming for a station while it has not yet departed that station, or departed it within the last two minutes; cancelled journeys SHALL stay listed with status `cancelled`, and early-terminated journeys with status `terminated` at the stations after their last served stop [A], until their scheduled departure has passed by ten minutes.

#### Scenario: Train just left
- **WHEN** a train departed the station one minute ago
- **THEN** it is still listed with its actual departure

#### Scenario: Cancelled train
- **WHEN** a journey is cancelled and its scheduled departure is five minutes ago
- **THEN** it is still listed as `cancelled`

#### Scenario: Train ended early [A]
- **WHEN** a journey was ended early at stop 3 and a passenger asks for departures from stop 4's station before its scheduled time
- **THEN** it is listed as `terminated` with no expected departure

### Requirement: Journey detail
The system SHALL provide an anonymous endpoint returning one journey with every stop in order — calling flag, scheduled and expected times, actual departure, current platform, platform-change time — plus last station passed, next station, minutes early or late, status and last-updated time; non-calling stops SHALL be marked as not calling (PAX-03, PAX-04, M-10).

#### Scenario: Calling stops
- **WHEN** a client opens a limited-stop journey
- **THEN** skipped stations are marked `isCalling: false` and carry no times

#### Scenario: Unknown journey
- **WHEN** the ID does not exist
- **THEN** the response is HTTP 404

### Requirement: Freshness on every item
Every item the public API returns SHALL carry the last-updated time of the live state it was built from and the time the response was generated, so a client can show "last updated" and mark data stale (M-02, PAX-09).

#### Scenario: Response timestamps
- **WHEN** any public live endpoint responds
- **THEN** each journey has `lastUpdatedAt` and the envelope has `generatedAt`

### Requirement: Platform changes and corrections visible
A journey with an uncorrected platform change SHALL show the new platform with the time the change became known and the provenance `automatically_published`; a journey whose change was corrected SHALL show the corrected state with a `correctedAt` time and no trace of the false state in its current values (M-09, PAX-05, PAX-10, PAX-12).

#### Scenario: Platform change flagged
- **WHEN** a stop's platform was changed from 2 to 3 at 07:41
- **THEN** the stop shows platform 3, `platformChangedAt` 07:41 and the provenance label

#### Scenario: Corrected cancellation
- **WHEN** a cancellation is corrected at 08:10
- **THEN** the journey's status is its restored status and `correctedAt` is 08:10

### Requirement: Passenger data not collected
Public endpoints SHALL require no identifying input, SHALL set no cookies and SHALL NOT log the caller's IP address or query values that identify a person (M-06, EP-14).

#### Scenario: Anonymous request
- **WHEN** a client calls a public endpoint
- **THEN** the response sets no cookie and the log entry has no IP

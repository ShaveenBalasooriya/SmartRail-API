# Spec Delta

## Purpose

Turns the timetable into dated operational journeys — the daily instances passengers follow, guards log and the Network Controller changes — without anyone entering them by hand (EP-04, SCH-08 carried).

## ADDED Requirements

### Requirement: Journeys generated for today and tomorrow
The system SHALL ensure that, for every service, one operational journey exists for today and one for tomorrow, where days are calendar days in the railway's configured time zone. Generation SHALL run at start-up and again periodically, so a new day's journeys appear within an hour of local midnight.

#### Scenario: Fresh start
- **WHEN** the application starts against a seeded database with no journeys
- **THEN** each service has a journey dated today and one dated tomorrow

#### Scenario: Day rolls over
- **WHEN** local midnight passes while the application is running
- **THEN** journeys for the new tomorrow are generated within an hour

### Requirement: Generation is idempotent
At most one journey SHALL exist per service per date; running generation again SHALL create no duplicates (EP-04).

#### Scenario: Restart on the same day
- **WHEN** the application restarts twice in one day
- **THEN** each service still has exactly one journey for today

### Requirement: Journey carries its scheduled departure instant
Each journey SHALL record its date and the UTC instant of its first stop's scheduled departure, computed from the local time in the railway's time zone (NC-01 ordering, data-model.md).

#### Scenario: Local to UTC
- **WHEN** a service departs at 07:30 local time in a UTC+05:30 time zone
- **THEN** its journey's scheduled departure instant is 02:00Z on that date

### Requirement: New journeys are announced
Each newly created journey SHALL be announced once to other modules with its ID, service, date and full stop baseline (scheduled UTC times, calling flags, platforms) and its guard, so Live Operations can create its live state (architecture §3).

#### Scenario: Live state created
- **WHEN** a journey is generated
- **THEN** Live Operations receives exactly one announcement for it

#### Scenario: Generation re-run
- **WHEN** generation runs again and finds the journey already exists
- **THEN** no second announcement is made

### Requirement: Scheduled journeys readable by date
The system SHALL provide an anonymous endpoint returning the scheduled journeys of a given date (today or tomorrow), each with service, line, direction, calling stops and scheduled times; other dates SHALL return HTTP 400 (EP-04 Done when).

#### Scenario: Today's journeys
- **WHEN** a client requests the scheduled journeys for today
- **THEN** every service's journey for today is returned in scheduled-departure order

#### Scenario: Date out of range
- **WHEN** a client requests journeys for a date next week
- **THEN** the response is HTTP 400

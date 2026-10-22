# Spec Delta

## Purpose

Turns the Guard's tap on "Departed" into the authoritative actual departure of each stop, accepted exactly once even when it was queued offline and synced later (GRD-02, GRD-05, GRD-07, M-23, M-24).

## ADDED Requirements

### Requirement: Log a departure
An assigned Guard SHALL be able to record a departure for one calling stop of a journey, giving a client-generated ID, the journey, the stop sequence, the device timestamp of the tap and an optional delay cause. An accepted log SHALL return HTTP 201 and SHALL update the journey's live state (GRD-02).

#### Scenario: Departure on time
- **WHEN** the assigned guard logs stop 1 of a running or scheduled journey
- **THEN** the response is 201 and the stop's actual departure equals the submitted device time

### Requirement: Exactly-once by client ID
A log whose client ID was already accepted with identical content SHALL return HTTP 200 with the original record and change nothing; the same ID with different content SHALL return HTTP 409 (GRD-07, M-23).

#### Scenario: Retry after lost response
- **WHEN** the app resends a log it already sent because the first response never arrived
- **THEN** the response is 200, no second departure is recorded and no new journey event is produced

#### Scenario: ID reused with different content
- **WHEN** a log arrives with an accepted ID but a different stop
- **THEN** the response is HTTP 409 and nothing changes

### Requirement: One departure per stop
A stop SHALL have at most one recorded departure; a second log for the same journey and stop with a different client ID SHALL be refused with HTTP 409 (GRD-02).

#### Scenario: Double tap with two IDs
- **WHEN** two logs with different IDs arrive for the same stop
- **THEN** the first is accepted and the second receives HTTP 409

### Requirement: Original timestamp is authoritative
The submitted device timestamp SHALL be stored as the departure time and the server time of receipt SHALL be stored separately; progress SHALL use the device timestamp (GRD-05, GRD-07).

#### Scenario: Offline for twenty minutes
- **WHEN** logs tapped at 08:02 and 08:09 arrive together at 08:25
- **THEN** the stops' actual departures are 08:02 and 08:09 and each log records 08:25 as received

### Requirement: Only valid stops
A log SHALL be refused with HTTP 400 when the stop is not a calling stop of the journey, when it is the journey's last calling stop, or when the stop sequence does not exist (GRD-02).

#### Scenario: Non-calling station
- **WHEN** a guard logs a station the service passes without stopping
- **THEN** the response is HTTP 400 naming the stop

#### Scenario: Terminus
- **WHEN** a guard logs a departure at the journey's final stop
- **THEN** the response is HTTP 400

### Requirement: Plausible timestamps
A log SHALL be refused with HTTP 400 when its timestamp lacks an offset, is more than two minutes ahead of server time, falls before the journey's service date began in the railway time zone, or is earlier than an already recorded departure at a previous stop or later than one at a following stop.

#### Scenario: Clock far ahead
- **WHEN** a log is timestamped ten minutes in the future
- **THEN** the response is HTTP 400

#### Scenario: Departures out of time order
- **WHEN** stop 2 is logged at 08:10 and stop 3 is then logged at 08:05
- **THEN** the stop 3 log is refused with HTTP 400

### Requirement: Journeys open for logging
Logs SHALL be accepted for journeys dated today or yesterday in the railway time zone, so a queue synced after midnight is not lost; other dates SHALL be refused with HTTP 400. Logs for a cancelled or terminated journey SHALL be accepted and recorded but SHALL NOT change its status.

#### Scenario: Sync after midnight
- **WHEN** a log for yesterday's journey arrives at 00:15
- **THEN** it is accepted

#### Scenario: Journey cancelled while offline
- **WHEN** a log arrives for a journey cancelled after the tap
- **THEN** it is recorded with HTTP 201 and the journey stays `cancelled`

#### Scenario: Journey terminated while offline [A]
- **WHEN** a log arrives for a journey the controller ended early after the tap
- **THEN** it is recorded with HTTP 201 and the journey stays `terminated`

### Requirement: Only the assigned guard
A log for a journey not assigned to the calling guard SHALL be refused with HTTP 403 and announced as a denied write; other roles SHALL receive HTTP 403 (GRD-09, M-04).

#### Scenario: Guard logs another guard's journey
- **WHEN** guard B logs a stop on a journey assigned to guard A
- **THEN** the response is HTTP 403 and a denied entry appears in the audit record

### Requirement: Delay cause from a fixed list
The delay cause SHALL be optional and, when given, SHALL be one of `signalling`, `rolling_stock`, `crew`, `passenger_boarding`, `infrastructure`, `weather` or `other`; any other value SHALL be refused with HTTP 400. Causes record service impact only (GRD-03).

#### Scenario: No cause
- **WHEN** a late departure is logged without a cause
- **THEN** it is accepted

#### Scenario: Unknown cause
- **WHEN** a log gives the cause `accident`
- **THEN** the response is HTTP 400

### Requirement: Logging never gates departure
The server SHALL accept a valid log regardless of the journey's current published state other than the rules above, and SHALL never return an instruction that holds or delays a train (GRD-04).

#### Scenario: Journey delayed by the controller
- **WHEN** a guard logs a departure of a journey with a published delay
- **THEN** the log is accepted and the departure becomes the most recent fact

### Requirement: Accepted logs are audited
Each accepted log SHALL produce one audit entry naming the guard, the journey, the stop and the departure time; replays SHALL NOT produce another (M-03, AUD-01).

#### Scenario: Replay not re-audited
- **WHEN** an identical log is resent
- **THEN** the audit record still holds exactly one entry for it

### Requirement: Response lets the app show queue status
Every accepted or replayed log response SHALL return the stored record with its client ID, stop, device time and server receipt time, so the app can mark that queued item as synced (GRD-06). Showing pending versus synced is a client feature.

#### Scenario: Marking an item synced
- **WHEN** the app receives 201 or 200 for a queued log
- **THEN** the body contains the client ID and `receivedAt`, which is enough to mark that item synced

### Requirement: Journeys complete automatically
A `running` journey whose penultimate calling stop has a recorded departure SHALL become `completed` once its expected arrival at the terminus has passed, with no person acting; one `journey_completed` event SHALL be appended in the same transaction and announced after commit (EP-07, M-07; live-running-state "Lifecycle status", journey-event-stream "One event per state change"). A periodic background check [A] performs this, as the passing of time is not a recorded fact.

#### Scenario: Final leg arrives
- **WHEN** a journey has departed its penultimate calling stop and its expected arrival at the terminus passes
- **THEN** within one check interval its status is `completed` and one `journey_completed` event exists for it

#### Scenario: Already completed
- **WHEN** the periodic check runs again for a journey that is already `completed`
- **THEN** no further event is appended

#### Scenario: Journey ended early [A]
- **WHEN** the controller has ended a journey early and the periodic check runs
- **THEN** the journey stays `terminated` and no `journey_completed` event is appended

#### Scenario: Penultimate stop never logged
- **WHEN** the expected arrival at the terminus passes but the penultimate calling stop has no recorded departure
- **THEN** the journey stays `running` (the Guard's log is the only source of progress)

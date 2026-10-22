# Spec Delta

## Purpose

Defines how SmartRail derives the single live picture of each journey — status, progress, expected times, delay, platforms and freshness — from its timetable baseline, the Guard's departure logs and the Network Controller's published changes (EP-07, M-01, M-08).

## ADDED Requirements

### Requirement: Live state created for every journey
When a journey is announced, the system SHALL create its live state with lifecycle status `scheduled`, zero delay, expected times equal to scheduled times, the baseline platforms, the assigned guard and a last-updated time equal to the creation time. (EP-07).

#### Scenario: Journey announced
- **WHEN** a journey is generated and announced
- **THEN** its live state exists with status `scheduled` and expected times equal to scheduled times

### Requirement: State derived only from recorded facts
A journey's live state SHALL be a deterministic function of its baseline, its recorded departures and its published changes that have not been corrected. Recomputing from the same facts SHALL give the same state, and a corrected change SHALL affect the state as if it had never been published (S-12, NC-06).

#### Scenario: Correction removes an effect
- **WHEN** a cancellation is published and then corrected
- **THEN** the journey's state is the same as if the cancellation had never been published

### Requirement: Actual departures
Each stop with a recorded departure SHALL show that departure's original device timestamp as its actual departure, regardless of when the record reached the server (GRD-07, M-23).

#### Scenario: Log synced late
- **WHEN** a departure tapped at 08:02 reaches the server at 08:20
- **THEN** the stop's actual departure is 08:02

### Requirement: Delay applied to stops not yet departed
For each calling stop without an actual departure, the delay SHALL be taken from the most recently recorded fact that covers it: a departure at an earlier stop covers every later stop with its deviation from schedule, in whole minutes rounded to nearest; a published delay covers its stop and every later stop. With no covering fact the delay SHALL be zero (NC-03, NC-07).

#### Scenario: Late departure propagates
- **WHEN** a journey departs stop 2 eight minutes after its scheduled departure
- **THEN** every later stop's expected times are its scheduled times plus eight minutes

#### Scenario: Controller delay after a guard log
- **WHEN** a guard log puts the journey 3 minutes late and the controller then publishes a 15-minute delay from stop 4
- **THEN** stop 3 keeps 3 minutes and stops 4 onward show 15 minutes

#### Scenario: Guard log after a controller delay
- **WHEN** a 15-minute delay from stop 3 is published and the guard then logs stop 3's departure 10 minutes late
- **THEN** stops after stop 3 show 10 minutes, because the departure is the more recent fact

### Requirement: Early running
A negative deviation SHALL make expected arrivals earlier than scheduled, but an expected departure SHALL never be earlier than the scheduled departure.

#### Scenario: Two minutes early
- **WHEN** a journey departs stop 1 two minutes early
- **THEN** stop 2's expected arrival is two minutes early and its expected departure is its scheduled departure

### Requirement: Journey-level delay and progress
The journey SHALL report minutes early or late as the delay of its next calling stop, its last departed station as the stop with the highest sequence that has an actual departure, and its next station as the first calling stop after that, or the first calling stop if none has departed (M-07, M-08, PAX-04).

#### Scenario: Mid-journey
- **WHEN** departures are recorded at stops 1 and 2
- **THEN** the last departed station is stop 2's station and the next station is stop 3's

#### Scenario: Departures recorded out of order
- **WHEN** the departure for stop 3 is recorded before the one for stop 2
- **THEN** the last departed station is stop 3's, and stop 2's actual departure is filled in when its record arrives

### Requirement: Current platform per stop
Each stop's current platform SHALL be the platform of its most recent uncorrected platform change, with the time that change was published, or the baseline platform with no change time (M-09, PAX-05).

#### Scenario: Platform changed then corrected
- **WHEN** a stop's platform is changed from 2 to 3 and that change is then corrected
- **THEN** the stop's current platform is 2 and it has no platform-change time

### Requirement: Lifecycle status
A journey SHALL be `scheduled` until its first departure is recorded, then `running`; `cancelled` while an uncorrected cancellation exists; and `completed` once a departure has been recorded at the last calling stop before the terminus and the expected arrival time at the terminus has passed (M-07, EP-07).

#### Scenario: First departure
- **WHEN** the first departure of a scheduled journey is recorded
- **THEN** its status becomes `running`

#### Scenario: Arrival time passes
- **WHEN** a journey has departed its penultimate calling stop and the clock passes its expected arrival at the terminus
- **THEN** its status becomes `completed` within one minute

#### Scenario: Never departed
- **WHEN** no departure is ever recorded for a journey
- **THEN** it stays `scheduled` and its last-updated time does not advance

### Requirement: Cancelled journeys keep scheduled times
A cancelled journey SHALL keep its scheduled times and SHALL report no expected times, no next station and no delay (NC-04, PAX-06).

#### Scenario: Cancelled before departure
- **WHEN** a scheduled journey is cancelled
- **THEN** its status is `cancelled`, every stop keeps its scheduled times and no expected times are reported

### Requirement: Freshness
Each journey SHALL carry a last-updated time equal to the server time at which the most recent fact affecting it was recorded, or its creation time when none has been (M-02).

#### Scenario: Guard log arrives
- **WHEN** a departure is recorded at server time 08:20
- **THEN** the journey's last-updated time is 08:20

### Requirement: Live state readable by other modules
Live state SHALL be readable through an in-process query contract: one journey with its stops; journeys by date; and journeys calling at a station within a time window (architecture §3).

#### Scenario: Station query
- **WHEN** another module asks for today's journeys calling at a station after 08:00
- **THEN** it receives each such journey with its live stop data, ordered by expected departure from that station

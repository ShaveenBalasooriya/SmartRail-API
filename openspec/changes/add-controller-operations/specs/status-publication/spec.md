# Spec Delta

## Purpose

Lets the Network Controller publish the three structured changes MVP 1 supports — delay, cancellation and platform change — so they take effect at once on every channel, labelled with how they were produced, attributed and audited (M-16, NC-03, NC-04, NC-05).

## ADDED Requirements

### Requirement: Only today's open journeys can be changed
A change SHALL be accepted only for a journey dated today in the railway time zone whose status is `scheduled` or `running`; a journey of another date SHALL be refused with HTTP 400 and a cancelled or completed journey with HTTP 409 (mvp-1-scope.md Roles: owns every change to today's journeys).

#### Scenario: Tomorrow's journey
- **WHEN** the controller publishes a delay on a journey dated tomorrow
- **THEN** the response is HTTP 400 and nothing changes

#### Scenario: Completed journey
- **WHEN** the controller publishes a platform change on a completed journey
- **THEN** the response is HTTP 409

### Requirement: Publish a delay
The controller SHALL be able to publish a delay of a whole number of minutes from 1 to 720, starting at a calling stop that has not yet departed; it takes effect immediately on that stop and every later stop (NC-03).

#### Scenario: Delay from the next stop
- **WHEN** the controller publishes a 15-minute delay from stop 3 of a running journey
- **THEN** the response is 201 and stops 3 onward show expected times 15 minutes after scheduled

#### Scenario: Delay at a departed stop
- **WHEN** the delay's starting stop has a recorded departure
- **THEN** the response is HTTP 409

#### Scenario: Out-of-range minutes
- **WHEN** the delay is 0 or 721 minutes
- **THEN** the response is HTTP 400

### Requirement: Publish a cancellation
The controller SHALL be able to cancel a journey that has no recorded departure; it becomes `cancelled` immediately. A journey with a recorded departure SHALL be refused with HTTP 409, because cancelling a running journey is early termination, which is deferred (NC-04).

#### Scenario: Cancel before departure
- **WHEN** the controller cancels a scheduled journey
- **THEN** the response is 201 and its status is `cancelled`

#### Scenario: Cancel a running journey
- **WHEN** the controller cancels a journey with a logged departure
- **THEN** the response is HTTP 409

### Requirement: Publish a platform change
The controller SHALL be able to change the platform of a calling stop that has not yet departed to another platform of that stop's station; the change takes effect immediately with its publish time as the time it became known (NC-05, M-09).

#### Scenario: Valid platform change
- **WHEN** the controller moves stop 2 from platform 2 to platform 3 of the same station
- **THEN** the response is 201 and stop 2's current platform is 3 with the publish time

#### Scenario: Platform of another station
- **WHEN** the new platform belongs to a different station
- **THEN** the response is HTTP 400

#### Scenario: Same platform
- **WHEN** the new platform equals the stop's current platform
- **THEN** the response is HTTP 409

### Requirement: Provenance, attribution and publish time
Every published change SHALL record the publishing controller, the server publish time and the provenance label `automatically_published`, and these SHALL be returned in the response and carried to every channel (M-16, PAX-10, CAT-05).

#### Scenario: Response content
- **WHEN** any change is published
- **THEN** the response includes the change ID, type, values, publisher, publish time and provenance `automatically_published`

### Requirement: Published without review
A valid change SHALL take effect when it is accepted; there SHALL be no pause, hold or approval step before it reaches the event stream (W-08, W-09).

#### Scenario: Immediate effect
- **WHEN** a change is accepted
- **THEN** its journey event exists by the time the response is returned

### Requirement: Changes audited
Every published change SHALL produce one audit entry with the controller, role, change type, journey and the before and after values of the affected fields (M-03, AUD-01).

#### Scenario: Platform change audited
- **WHEN** a platform change is published
- **THEN** the audit entry's before value is the old platform and its after value is the new one

### Requirement: Controller-only writes
Only the `network_controller` role SHALL publish changes; Guard and Auditor tokens SHALL receive HTTP 403 and the attempt SHALL be recorded as a denied write (NC-08, GRD-09, AUD-06).

#### Scenario: Guard tries to cancel
- **WHEN** a Guard token posts a cancellation
- **THEN** the response is HTTP 403, nothing changes and a denied audit entry exists

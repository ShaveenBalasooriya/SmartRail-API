# Spec Delta

## Purpose

Proves, through the system's real interfaces and the full local stack, the five flows that carry MVP 1's claim: a change made once reaches every channel, labelled, ordered and audited, and only the right role can make it (brief §23, mvp-1-scope.md five flows).

## ADDED Requirements

### Requirement: Flow 1, a delay reaches every channel
With a running journey, when the Network Controller publishes a delay, the expected times at every later stop SHALL update on the public API, the display feed and the Guard's journey view and stream, the simulated provider SHALL receive and apply the event, and the audit record SHALL hold the change.

#### Scenario: Delay everywhere
- **WHEN** the controller publishes a 15-minute delay from stop 3 of a running journey
- **THEN** within five seconds the public stream, the display feed, the Guard stream and the simulated provider all show stops 3 onward 15 minutes later, and the audit list contains one entry for it

### Requirement: Flow 2, offline guard logs sync once
A Guard's departure logs queued while offline SHALL, when posted later, produce exactly one actual departure each with its original timestamp, and the passenger and display data SHALL show the progress computed from those timestamps.

#### Scenario: Two queued logs
- **WHEN** a guard posts logs for stops 1 and 2 with device times from twenty minutes earlier, then posts the same two again
- **THEN** each stop has one actual departure with the original time, the replays return 200 without new events, and the public journey detail shows stop 2's station as last passed

### Requirement: Flow 3, platform change everywhere
A platform change published by the controller SHALL show on the public API, the display feed (moved to the new platform) and the Guard's view with the time it became known and the provenance label, and SHALL be delivered to the simulated provider.

#### Scenario: Platform 2 to 3
- **WHEN** the controller moves a stop from platform 2 to 3
- **THEN** the public journey shows platform 3 with the change time and `automatically_published`, the display feed for platform 3 lists it flagged, and the simulated provider's log shows the event applied

### Requirement: Flow 4, separation of duties denials
A Guard token publishing a disruption and an Auditor token attempting any change SHALL each be refused with HTTP 403 without changing any state, and both denials SHALL appear in the audit record with actor, role, attempted action and time.

#### Scenario: Two denials
- **WHEN** a Guard token posts a delay and an Auditor token posts a cancellation
- **THEN** both receive 403, the journey is unchanged, and the Auditor's list shows two `denied` entries naming the guard and the auditor

### Requirement: Flow 5, wrong cancellation corrected
A cancellation published in error and then corrected SHALL show as corrected on every channel, including the provider, and the audit record SHALL keep both the original and the correction.

#### Scenario: Cancel then correct
- **WHEN** the controller cancels a journey and then corrects the cancellation
- **THEN** the public API, display feed, Guard view and simulated provider all show the journey restored with a corrected note, and the audit holds both entries with the correction linked to the original

### Requirement: Real stack, real credentials
End-to-end flows SHALL run against the Compose stack with the identity provider issuing the tokens and the provider using its API key; they SHALL NOT bypass authentication or call internal services directly.

#### Scenario: Token source
- **WHEN** a flow acts as a guard
- **THEN** its bearer token was issued by the identity provider for the seeded guard account

### Requirement: Deterministic and independent
Each flow SHALL create or select its own journey and SHALL pass when run alone, in any order, and repeatedly, without depending on another flow's state or on wall-clock time of day (a test clock or its own seeded journeys are used).

#### Scenario: Run twice
- **WHEN** the suite runs twice in a row without resetting the stack
- **THEN** every flow passes both times

### Requirement: Run in CI
The five flows SHALL run in CI on every promotion to `main` and on request for `dev`; a failing flow SHALL block the promotion (EP-16 Done when).

#### Scenario: Failing flow
- **WHEN** any flow fails in the promotion pipeline
- **THEN** the pipeline fails and `main` is not updated

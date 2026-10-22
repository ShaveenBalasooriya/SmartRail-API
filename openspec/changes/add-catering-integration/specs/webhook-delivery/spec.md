# Spec Delta

## Purpose

Pushes each railway change to the provider reliably — in order, without duplicates and surviving the provider's outages — so a cancellation reaches the kitchen once and a wrong cancellation is undone (CAT-05 to CAT-09, M-26).

## ADDED Requirements

### Requirement: Pushed event types
SmartRail SHALL push delay, cancellation, early-termination, platform-change and correction events to every active subscription, because the provider must learn that a train will not reach its later stations (CAT-05, CAT-09; early termination by owner decision 2026-10-21, [A]); departure events and journey-completed events SHALL NOT be pushed.

#### Scenario: Departure logged
- **WHEN** a guard logs a departure
- **THEN** no delivery is created

#### Scenario: Cancellation published
- **WHEN** the controller cancels a journey
- **THEN** one delivery is created for each active subscription

#### Scenario: Early termination published [A]
- **WHEN** the controller ends a running journey early
- **THEN** one delivery is created for each active subscription, naming the last stop served in `stopSequence`

### Requirement: Event payload
Each delivery SHALL be an HTTP POST of JSON carrying the event ID, the per-journey sequence number, the event type, the journey, the stop where relevant, the new values, the published time, the provenance label, and for a correction the original event's ID (CAT-05, CAT-09).

#### Scenario: Platform change
- **WHEN** a platform change is delivered
- **THEN** the payload names the journey, the stop, the new platform, the published time and `automatically_published`

#### Scenario: Correction
- **WHEN** a correction is delivered
- **THEN** the payload carries the original event's ID

### Requirement: Persisted before sending
Every committed event of a pushed type SHALL eventually result in a delivery record for each active subscription, even across a restart, and the record SHALL exist in the database before the delivery is sent, so no committed change is lost (owner decision 2026-10-21, `../docs/architecture.md` §3; CAT-05). A Catering consumer of the event bus creates the records, so a record may appear a moment after the commit [A].

#### Scenario: Restart before send
- **WHEN** the application restarts with pending deliveries
- **THEN** they are sent after start-up

#### Scenario: Restart before the delivery record exists [A]
- **WHEN** an event commits and the application restarts before its delivery record is created
- **THEN** after start-up the bus delivers the event again, the record is created and the delivery is sent

### Requirement: One delivery per event per subscription
There SHALL be at most one delivery record per event per subscription, however many times the event is delivered to the consumer (CAT-06).

#### Scenario: Event delivered twice
- **WHEN** the bus delivers the same event to the Catering consumer twice, for example after a restart
- **THEN** one delivery record exists

### Requirement: Retry with backoff
A delivery answered with a network error, a timeout (10 seconds) or any status other than 2xx SHALL be retried with increasing delays (5 s, 30 s, 2 min, 10 min, 30 min) until it succeeds or reaches the retry limit (five attempts), after which it is marked `abandoned` (CAT-08).

#### Scenario: Provider down briefly
- **WHEN** the first attempt fails and the second succeeds
- **THEN** the delivery is marked `delivered` with two attempts

#### Scenario: Retry limit
- **WHEN** all five attempts fail
- **THEN** the delivery is marked `abandoned` and no further attempt is made

### Requirement: Order preserved per journey
Deliveries for one journey SHALL be sent in sequence-number order, and a later event SHALL NOT be sent while an earlier event of the same journey is still being retried; an abandoned delivery SHALL release the journey's queue (CAT-07).

#### Scenario: Earlier event failing
- **WHEN** event 4 of a journey is awaiting retry and event 5 is created
- **THEN** event 5 waits until event 4 is delivered or abandoned

#### Scenario: Other journeys unaffected
- **WHEN** one journey's delivery is retrying
- **THEN** deliveries for other journeys continue

### Requirement: Sequence numbers allow provider-side ordering
Delivered events SHALL carry the journey's sequence number from the journey event stream, which increases for each event of the journey but MAY skip numbers for events that are not pushed (CAT-07).

#### Scenario: Skipped numbers
- **WHEN** sequence 3 is a departure that was not pushed
- **THEN** the provider sees 2 then 4, which still increases

### Requirement: Failure visibility
SmartRail SHALL count delivery failures and abandoned deliveries as metrics and log each failed attempt with the subscription ID, the event ID, the attempt number and the response status, without logging the payload or the callback URL's query string (S-19, EP-17).

#### Scenario: Failed attempt
- **WHEN** an attempt returns HTTP 503
- **THEN** the failure counter increases and the log entry shows status 503 and no payload

### Requirement: Deactivated subscriptions
Pending deliveries of a subscription that has been deactivated or replaced SHALL be abandoned without being sent.

#### Scenario: Replaced URL
- **WHEN** the provider replaces its URL while deliveries are pending
- **THEN** the pending deliveries to the old URL are abandoned

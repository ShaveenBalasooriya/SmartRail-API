# Spec Delta

## Purpose

Tells a passenger what to do when something goes wrong, in plain words generated from fixed templates, with the next service to the same destination, so they can decide to wait or re-plan (M-11, PAX-06).

## ADDED Requirements

### Requirement: Guidance text per disruption type
A journey with an uncorrected delay, cancellation, early termination or platform change SHALL carry a guidance object with a plain-language message chosen from fixed templates for that type, filled with the journey's own values (minutes, new platform, destination); the text SHALL NOT be free-form or AI-generated (M-11, W-16).

#### Scenario: Cancellation
- **WHEN** a journey is cancelled
- **THEN** the guidance names the cancelled departure and tells the passenger to take the next service

#### Scenario: Early termination [A]
- **WHEN** a running journey is ended early at stop 3
- **THEN** the guidance names the last station served, says the train will not call at the stations after it, and tells the passenger to take the next service

#### Scenario: Platform change
- **WHEN** a platform change is published
- **THEN** the guidance names the new platform

#### Scenario: Undisrupted journey
- **WHEN** a journey has no uncorrected disruption
- **THEN** it carries no guidance

### Requirement: Delay guidance threshold
A delay SHALL produce guidance only when the journey is at least five minutes late; smaller deviations are shown as minutes late without guidance (S-04 thresholds deferred).

#### Scenario: Two minutes late
- **WHEN** a journey is two minutes late
- **THEN** no guidance is attached

### Requirement: Next service to the same destination
Guidance for a cancelled or early-terminated journey, and for a delay of 15 minutes or more, SHALL include the next journey that is neither cancelled nor terminated from the same origin to the same final destination that departs after the affected one, with its expected departure and platform; when none exists today, guidance SHALL say no later service is scheduled today.

#### Scenario: Another train follows
- **WHEN** the 08:00 service is cancelled and a 08:20 service runs the same route
- **THEN** guidance names the 08:20 departure with its expected time and platform

#### Scenario: Last train
- **WHEN** the cancelled journey was the last service of the day
- **THEN** guidance states that no later service is scheduled today

### Requirement: Corrections replace guidance
When a disruption is corrected, its guidance SHALL disappear in the same update that restores the journey, and the journey SHALL show a note that it was corrected (PAX-12).

#### Scenario: Cancellation corrected
- **WHEN** the cancellation is corrected
- **THEN** the journey carries no cancellation guidance and shows `correctedAt`

### Requirement: Text is meaningful on its own
Guidance messages SHALL be complete sentences that do not rely on colour, icons or position, so screen readers and text-only displays convey the same meaning (M-05, PAX-11).

#### Scenario: Screen reader
- **WHEN** a client reads only the guidance text
- **THEN** it conveys what happened and what to do without other visual cues

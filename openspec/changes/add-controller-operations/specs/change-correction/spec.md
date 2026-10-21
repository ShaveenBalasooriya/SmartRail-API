# Spec Delta

## Purpose

Lets the Network Controller undo a wrongly published change everywhere it went, while keeping the original on record, so a false cancellation never persists on any channel and the audit trail shows both (S-12, NC-06, AUD-03).

## ADDED Requirements

### Requirement: Correct a published change
The controller SHALL be able to correct a published delay, cancellation, early termination or platform change of one of today's journeys. The correction SHALL be recorded as a new change of type `correction` referencing the original, and the original SHALL remain recorded, unchanged and marked corrected (NC-06, S-12).

#### Scenario: Wrong cancellation
- **WHEN** the controller corrects a cancellation
- **THEN** the response is 201, the journey is no longer cancelled, and both the cancellation and the correction exist in its history

### Requirement: Correction restores the uncorrected state
After a correction, the journey's live state SHALL be what it would be had the original change never been published, keeping every other fact, including changes published after the original (S-12). A correction reverts the original change; it does not carry replacement values.

#### Scenario: Later change survives
- **WHEN** platform changes A then B are published on one stop and A is corrected
- **THEN** the stop shows B's platform

#### Scenario: Wrong early termination [A]
- **WHEN** an early termination is corrected
- **THEN** the journey is `running` again with the expected times its departures give

#### Scenario: Corrected delay
- **WHEN** a 30-minute delay is corrected
- **THEN** expected times return to what the departures alone give

### Requirement: What can be corrected
A correction SHALL be refused with HTTP 409 when the target is itself a correction or has already been corrected, and with HTTP 404 when the target change does not exist; corrections of corrections are not supported in MVP 1.

#### Scenario: Corrected twice
- **WHEN** the controller corrects a change that already has a correction
- **THEN** the response is HTTP 409

#### Scenario: Correcting a correction
- **WHEN** the controller targets a correction
- **THEN** the response is HTTP 409

### Requirement: Correction reaches every channel the original reached
A correction SHALL produce a journey event of type `correction_published` that references the original change's event, so every channel that received the original receives the correction through the same path (S-12, PAX-12, CAT-09).

#### Scenario: Event references original
- **WHEN** a correction is published
- **THEN** its journey event names the original change and the original's event ID

### Requirement: Correction labelled, attributed and audited
A correction SHALL carry the provenance `automatically_published`, the controller and the publish time, and SHALL produce an audit entry linked to the original change's audit entry (M-16, AUD-03).

#### Scenario: Linked audit entries
- **WHEN** the Auditor opens the correction's audit entry
- **THEN** it links to the original change's entry, which is unchanged

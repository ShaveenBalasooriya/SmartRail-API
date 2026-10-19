# Spec Delta

## Purpose

Keeps a tamper-evident, append-only record of every staff state change and every denied write attempt, so it can be proven who did what, in which role and when, without holding any passenger data (EP-03, M-03).

## ADDED Requirements

### Requirement: Every staff state change is recorded
Every successful state-changing action by a staff member SHALL produce exactly one audit entry with the actor's staff ID, the actor's role at that time, the action name, the outcome `succeeded`, the target type and ID, the before and after values, and the server timestamp (AUD-01, AUD-03).

#### Scenario: Controller publishes a delay
- **WHEN** a Network Controller publishes a delay on a journey
- **THEN** one entry records that controller, role `network_controller`, the publish action, the journey as target, the before and after values and the server time

### Requirement: State change and audit entry commit together
A state change SHALL NOT be committed without its audit entry, and an audit entry SHALL NOT be committed for a change that was rolled back.

#### Scenario: Audit write fails
- **WHEN** writing the audit entry fails during a status change
- **THEN** the status change is not committed and the request fails with HTTP 500

### Requirement: Denied write attempts are recorded
Every access-denied notification SHALL produce one audit entry with outcome `denied`, the staff ID when known, the role, the attempted action and the server time (AUD-04, NC-08, GRD-09).

#### Scenario: Guard tries to publish
- **WHEN** a Guard token is refused on the publish-delay endpoint
- **THEN** an entry with outcome `denied`, role `guard` and the attempted action appears in the record

### Requirement: Append-only record
No role and no application path SHALL update or delete an audit entry; the database itself SHALL refuse updates and deletes on audit entries from the application's database account (ADM-06 carried, AUD-06).

#### Scenario: Direct update attempt
- **WHEN** the application's database account runs an UPDATE on an audit entry
- **THEN** the database rejects it

#### Scenario: No delete endpoint
- **WHEN** any staff token sends DELETE to an audit endpoint
- **THEN** the response is 403 or 405 and no entry is removed

### Requirement: Hash-chained entries
Entries SHALL form one chain across the whole record, ordered by a gap-free chain position. Each entry's hash SHALL cover its content and the previous entry's hash, so altering any stored entry breaks the chain from that point on (AUD-05, owner decision).

#### Scenario: Concurrent writes
- **WHEN** two state changes are committed at the same moment
- **THEN** their entries receive consecutive positions and each links to the hash before it

### Requirement: Chain integrity check
The Auditor SHALL be able to request an integrity check that recomputes the chain and reports either `intact` with the number of entries checked, or `broken` with the first chain position that fails (AUD-05).

#### Scenario: Untouched record
- **WHEN** the Auditor runs the integrity check and no entry has been altered
- **THEN** the result is `intact` with the entry count

#### Scenario: Altered entry
- **WHEN** an entry's stored after-value has been changed directly in the database and the check runs
- **THEN** the result is `broken` and names that entry's chain position

### Requirement: Auditor list with filters
The Auditor SHALL be able to list entries newest first, paginated, filtered by actor, time range and outcome, with each item showing actor, role, action, outcome, target and server time (AUD-01, AUD-02).

#### Scenario: Filter by actor and time
- **WHEN** the Auditor filters by one guard and a one-hour range
- **THEN** only that guard's entries within the range are returned

#### Scenario: Invalid range
- **WHEN** the requested range's start is after its end
- **THEN** the response is HTTP 400

### Requirement: Entry detail with related entry
The Auditor SHALL be able to read one entry in full, including before and after values, and an entry for a correction SHALL link to the entry of the change it corrects (AUD-03).

#### Scenario: Correction entry
- **WHEN** the Auditor opens the entry for a correction
- **THEN** it shows the ID of the original change's entry, and that original entry is unchanged

### Requirement: Audit access is Auditor-only and read-only
Audit endpoints SHALL be readable only by the `auditor` role; Network Controller and Guard tokens SHALL receive HTTP 403 (AUD-06).

#### Scenario: Controller opens the audit list
- **WHEN** a Network Controller token requests the audit list
- **THEN** the response is HTTP 403

### Requirement: No passenger data in the audit record
Audit entries SHALL hold staff actions only and SHALL NOT contain passenger personal data (AUD-07, M-03).

#### Scenario: Automated check
- **WHEN** the test suite inspects entries produced by every recorded action
- **THEN** no entry contains passenger data, tokens, API keys or IP addresses

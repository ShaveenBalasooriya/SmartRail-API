# Spec Delta

## Purpose

Makes separation of duties enforceable and provable: every endpoint declares who may call it, the server refuses everyone else, and every refused write is announced so it can be audited (brief §14, M-04, EP-02).

## ADDED Requirements

### Requirement: Fixed role catalogue
The system SHALL support exactly three staff roles in MVP 1 — `network_controller`, `guard` and `auditor` — and each staff account SHALL hold exactly one role, stored by SmartRail (EP-02, mvp-1-scope.md Roles).

#### Scenario: Seed with two roles
- **WHEN** a seed record assigns two roles to one staff account
- **THEN** seeding fails and the application does not start

#### Scenario: Unknown role
- **WHEN** a seed record assigns a role outside the catalogue, such as `scheduler`
- **THEN** seeding fails and the application does not start

### Requirement: Roles come from SmartRail, not the token
Authorisation decisions SHALL use the role stored on the SmartRail staff account. Role or group claims inside the token SHALL be ignored (architecture §5).

#### Scenario: Token claims a different role
- **WHEN** a Guard's token carries a group claim naming `network_controller`
- **THEN** the request is authorised as `guard` only

### Requirement: Every endpoint has an explicit access policy
Every endpoint SHALL be classified as exactly one of: public (anonymous), provider (API key), or staff with a named set of allowed roles. An endpoint without a classification SHALL make the application fail to start.

#### Scenario: Unclassified endpoint
- **WHEN** a module maps an endpoint without declaring its access policy
- **THEN** the architecture test or start-up check fails

### Requirement: Wrong role is refused with 403
A staff request whose role is not allowed by the endpoint's policy SHALL be refused with HTTP 403 problem details and SHALL change nothing (NC-08, GRD-09, AUD-06).

#### Scenario: Guard publishes a disruption
- **WHEN** a Guard token calls the endpoint that publishes a delay
- **THEN** the response is HTTP 403 and no status change is recorded

#### Scenario: Auditor attempts any change
- **WHEN** an Auditor token calls any write endpoint
- **THEN** the response is HTTP 403 and nothing is changed

#### Scenario: Controller logs a departure
- **WHEN** a Network Controller token calls the Guard's departure-log endpoint
- **THEN** the response is HTTP 403

### Requirement: Denied writes are announced for audit
Every request refused with 403 on a state-changing endpoint SHALL publish an access-denied notification carrying the staff ID (when known), the role, the attempted action and the server time, so it can be written to the audit record (M-03, AUD-04). Denied reads SHALL NOT be announced.

#### Scenario: Denied write
- **WHEN** a Guard token is refused on the publish-delay endpoint
- **THEN** one access-denied notification is published naming the guard, the role `guard`, the action and the time

#### Scenario: Denied read
- **WHEN** a Guard token is refused on the controller's live-view endpoint
- **THEN** the response is 403 and no access-denied notification is published

### Requirement: Public endpoints need no credentials
Endpoints classified public SHALL serve anonymous requests and SHALL ignore any `Authorization` header sent to them (M-06, PAX-01).

#### Scenario: Anonymous passenger
- **WHEN** a request with no credentials calls a public departures endpoint
- **THEN** it is served normally

### Requirement: Staff and provider credentials are not interchangeable
A provider API key SHALL NOT authenticate a staff endpoint, and a staff token SHALL NOT authenticate a provider endpoint (CAT-01, least privilege).

#### Scenario: API key on a staff endpoint
- **WHEN** a request carries only a valid provider API key and calls a staff endpoint
- **THEN** the response is HTTP 401

#### Scenario: Staff token on a provider endpoint
- **WHEN** a request carries only a valid staff token and calls a provider endpoint
- **THEN** the response is HTTP 401

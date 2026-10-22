# Spec Delta

## Purpose

Makes separation of duties enforceable and provable: every endpoint declares who may call it, the server refuses everyone else, and every refused write is announced so it can be audited (brief §14, M-04, EP-02).

## ADDED Requirements

### Requirement: Fixed role catalogue
The system SHALL support exactly three staff roles in MVP 1 — `network_controller`, `guard` and `auditor` — assigned as identity-provider groups of the same names. A staff caller SHALL have a role only when its token names exactly one catalogue group (EP-02, mvp-1-scope.md Roles).

#### Scenario: Token with two role groups
- **WHEN** a staff token names both `guard` and `network_controller` and calls any staff endpoint
- **THEN** the response is HTTP 403

#### Scenario: Group outside the catalogue
- **WHEN** a staff token names only the group `scheduler`
- **THEN** the caller has no role and every staff endpoint returns HTTP 403

### Requirement: Roles come from the token's groups
Authorisation decisions SHALL use the catalogue group in the validated token's `groups` claim. Other groups and claims SHALL NOT grant a role (owner decision, 2026-10-20).

#### Scenario: Guard token
- **WHEN** a token in the `guard` group calls an endpoint allowed only for `network_controller`
- **THEN** the response is HTTP 403

### Requirement: Every endpoint has an explicit access policy
Every endpoint SHALL be classified as exactly one of: public (anonymous), provider (API key), or staff with a named set of allowed roles. An endpoint that declares no classification SHALL require an authenticated staff caller by default, so that it is never accidentally public.

#### Scenario: Endpoint without a classification
- **WHEN** a module maps an endpoint without declaring its access policy and an anonymous request calls it
- **THEN** the response is HTTP 401

### Requirement: Wrong role is refused with 403
A staff request whose role is not allowed by the endpoint's policy SHALL be refused with HTTP 403 problem details and SHALL change nothing (NC-08, GRD-09, AUD-06).

#### Scenario: Write by the wrong role
- **WHEN** a `guard` or `auditor` token calls a write endpoint allowed only for `network_controller`
- **THEN** the response is HTTP 403 and nothing is changed

### Requirement: Denied writes are announced for audit
Every request refused with 403 on a state-changing endpoint SHALL publish an access-denied notification carrying the staff ID (when known), the role (when known), the attempted action and the server time, so it can be written to the audit record (M-03, AUD-04). Denied reads SHALL NOT be announced.

#### Scenario: Denied write
- **WHEN** a `guard` token is refused on a controller-only POST endpoint
- **THEN** one access-denied notification is published naming the guard, the role `guard`, the action and the time

#### Scenario: Denied read
- **WHEN** a `guard` token is refused on a controller-only GET endpoint
- **THEN** the response is 403 and no access-denied notification is published

### Requirement: Public endpoints need no credentials
Endpoints classified public SHALL serve anonymous requests and SHALL ignore any `Authorization` header sent to them (M-06, PAX-01).

#### Scenario: Anonymous caller
- **WHEN** a request with no credentials calls the network stations endpoint
- **THEN** it is served normally

### Requirement: Staff and provider credentials are not interchangeable
A provider API key SHALL NOT authenticate a staff endpoint, and a staff token SHALL NOT authenticate a provider endpoint (CAT-01, least privilege).

#### Scenario: API key on a staff endpoint
- **WHEN** a request carries only a valid provider API key and calls a staff endpoint
- **THEN** the response is HTTP 401

#### Scenario: Staff token on a provider endpoint
- **WHEN** a request carries only a valid staff token and calls a provider endpoint
- **THEN** the response is HTTP 401

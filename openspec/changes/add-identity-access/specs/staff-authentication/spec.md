# Spec Delta

## Purpose

Establishes who a staff caller is: SmartRail accepts bearer tokens issued by its staff identity provider and links each one to a seeded SmartRail staff account, without ever storing a credential (architecture §5, EP-02).

## ADDED Requirements

### Requirement: Bearer token validation
A staff endpoint SHALL accept a request only when it carries an `Authorization: Bearer` token issued by the configured identity provider for the staff client, with a valid signature, the expected issuer and audience, and an unexpired lifetime (EP-02).

#### Scenario: Valid token
- **WHEN** a request to a staff endpoint carries an unexpired token from the staff client
- **THEN** the request is authenticated and continues to the authorisation check

#### Scenario: Missing token
- **WHEN** a request to a staff endpoint carries no `Authorization` header
- **THEN** the response is HTTP 401 problem details and nothing is changed

#### Scenario: Expired or tampered token
- **WHEN** the token is expired, has an invalid signature, or names another issuer or audience
- **THEN** the response is HTTP 401 and the token content is not logged

### Requirement: Token resolves to a seeded staff account
After a token is validated, the system SHALL resolve its subject to exactly one SmartRail staff account. A valid token whose subject matches no staff account SHALL be refused with HTTP 403.

#### Scenario: Known staff member
- **WHEN** a valid token's subject is `guard.ishara`, a seeded staff account, and the token is in the `guard` group
- **THEN** the request runs as that staff account with the role `guard`

#### Scenario: Unknown subject
- **WHEN** a valid token's subject matches no SmartRail staff account
- **THEN** the response is HTTP 403 and the request is treated as denied

### Requirement: No credentials stored by SmartRail
SmartRail SHALL NOT store staff passwords or long-lived staff credentials; it SHALL store only the identity provider's subject identifier, username and display name for each staff account (architecture §5, data-model.md).

#### Scenario: Inspecting the identity schema
- **WHEN** the `identity` schema is inspected after seeding
- **THEN** no column holds a staff password, password hash or refresh token

### Requirement: Current user available to modules
For every authenticated staff request the system SHALL make the caller's staff account ID and role available to any module's handlers through a Shared Kernel port, so that state changes and audit entries can be attributed (EP-03, M-03).

#### Scenario: Attributed caller
- **WHEN** an authenticated Network Controller calls `GET /identity/me`
- **THEN** the response holds that controller's staff ID and the role `network_controller`

### Requirement: Seeded staff accounts
Staff accounts SHALL be loaded from seed data, and the identity provider SHALL be configured from a checked-in declarative configuration that defines the staff client, the three role groups and the matching staff users, each in one group (mvp-1-scope.md Owner decisions).

#### Scenario: Fresh stack
- **WHEN** the local stack starts from empty volumes
- **THEN** each seeded staff user can obtain a token from the identity provider and that token resolves to the matching SmartRail account and role

# Spec Delta

## Purpose

Lets a developer sign in and call protected endpoints directly from the Scalar API reference, using the same credentials real clients use, so protected behaviour can be explored and demonstrated without separate tools.

## ADDED Requirements

### Requirement: Security schemes documented
The OpenAPI document SHALL declare an OAuth2 authorisation-code scheme with PKCE for staff endpoints and an API-key scheme (`X-Api-Key`) for provider endpoints. Each endpoint SHALL reference the scheme that applies to it, and public endpoints SHALL reference none.

#### Scenario: Staff endpoint
- **WHEN** the document is inspected for a staff endpoint
- **THEN** it requires the staff OAuth2 scheme

#### Scenario: Public endpoint
- **WHEN** the document is inspected for a public endpoint
- **THEN** it declares no security requirement

### Requirement: Sign in from Scalar as staff
In an environment where Scalar is enabled, a developer SHALL be able to choose the staff scheme in Scalar, be sent to the identity provider to sign in as a seeded staff user, and return to Scalar with a bearer token that Scalar then sends with requests. No client secret SHALL be held in the browser.

#### Scenario: Controller signs in through Scalar
- **WHEN** a developer signs in through Scalar as the seeded Network Controller and calls the live-view endpoint
- **THEN** the request carries a bearer token and returns HTTP 200

#### Scenario: Wrong role through Scalar
- **WHEN** a developer signs in as the seeded Guard and calls a controller-only endpoint
- **THEN** the response is HTTP 403, exactly as for any other client

### Requirement: Dedicated public client for Scalar
The identity provider SHALL define a separate public OAuth2 client for the API reference, with the Scalar callback address as its only redirect URI and with PKCE required, so the reference's sign-in is distinct from the staff client used by real applications. Tokens it issues SHALL be accepted by the staff endpoints as staff tokens and SHALL resolve to the same seeded staff accounts.

#### Scenario: Token accepted
- **WHEN** a token issued to the Scalar client is sent to a staff endpoint
- **THEN** it is validated like any other staff token and resolved to the seeded account

#### Scenario: Unknown redirect
- **WHEN** a sign-in is attempted with any other redirect address
- **THEN** the identity provider refuses it

### Requirement: Provider key in Scalar
A developer SHALL be able to enter the provider API key in Scalar for the API-key scheme and call provider endpoints with it; the key SHALL be entered at run time and SHALL NOT be pre-filled by the application.

#### Scenario: Provider call
- **WHEN** a developer enters a valid provider key in Scalar and lists stations
- **THEN** the call succeeds with HTTP 200

#### Scenario: No pre-filled secrets
- **WHEN** the reference page source and the OpenAPI document are inspected
- **THEN** neither contains a token, client secret or API key

### Requirement: Sign-in only where documentation is enabled
The Scalar client SHALL be configured and the security schemes SHALL be served only in environments where the reference is enabled.

#### Scenario: Production
- **WHEN** the application runs in an environment where documentation is disabled
- **THEN** no Scalar client configuration is served

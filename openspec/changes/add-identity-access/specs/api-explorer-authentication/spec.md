# Spec Delta

## Purpose

Lets a developer call protected endpoints directly from the Scalar API reference with the same credentials real clients use, so protected behaviour can be explored and demonstrated without separate tools.

## ADDED Requirements

### Requirement: Security schemes documented
The OpenAPI document SHALL declare an OAuth2 authorization-code scheme for staff endpoints, pointing at the identity provider's authorization and token URLs, and an API-key scheme (`X-Api-Key`) for provider endpoints. Each endpoint SHALL reference the scheme that applies to it, and public endpoints SHALL reference none.

#### Scenario: Staff endpoint
- **WHEN** the document is inspected for a staff endpoint
- **THEN** it requires the staff OAuth2 scheme

#### Scenario: Public endpoint
- **WHEN** the document is inspected for a public endpoint
- **THEN** it declares no security requirement

### Requirement: Staff sign-in from Scalar
In an environment where Scalar is enabled, a developer SHALL be able to sign in from Scalar as a seeded staff user through the identity provider, using the authorization code flow with PKCE and the same public staff client the frontends use, and Scalar SHALL send the resulting token with requests. The token is checked exactly as for any other client.

#### Scenario: Staff sign-in through Scalar
- **WHEN** a developer signs in from Scalar as the seeded Network Controller and calls `GET /identity/me`
- **THEN** the response is HTTP 200 with the role `network_controller`

#### Scenario: No sign-in through Scalar
- **WHEN** a developer calls `GET /identity/me` in Scalar without signing in
- **THEN** the response is HTTP 401, exactly as for any other client

### Requirement: Provider key in Scalar
A developer SHALL be able to enter the provider API key in Scalar for the API-key scheme and call provider endpoints with it. Neither the reference page nor the OpenAPI document SHALL contain a token or API key pre-filled by the application.

#### Scenario: Provider call
- **WHEN** a developer enters a valid provider key in Scalar and calls a provider endpoint
- **THEN** the call succeeds with HTTP 200

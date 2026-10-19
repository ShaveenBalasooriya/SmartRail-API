# Spec Delta

## Purpose

Lets a developer call protected endpoints directly from the Scalar API reference with the same credentials real clients use, so protected behaviour can be explored and demonstrated without separate tools.

## ADDED Requirements

### Requirement: Security schemes documented
The OpenAPI document SHALL declare an HTTP bearer scheme for staff endpoints and an API-key scheme (`X-Api-Key`) for provider endpoints. Each endpoint SHALL reference the scheme that applies to it, and public endpoints SHALL reference none.

#### Scenario: Staff endpoint
- **WHEN** the document is inspected for a staff endpoint
- **THEN** it requires the staff bearer scheme

#### Scenario: Public endpoint
- **WHEN** the document is inspected for a public endpoint
- **THEN** it declares no security requirement

### Requirement: Staff token entered in Scalar
In an environment where Scalar is enabled, a developer SHALL be able to paste a staff bearer token, obtained from the identity provider for a seeded staff user, into Scalar, and Scalar SHALL send it with requests. The token is checked exactly as for any other client.

#### Scenario: Controller token through Scalar
- **WHEN** a developer enters the seeded Network Controller's token in Scalar and calls the live-view endpoint
- **THEN** the response is HTTP 200

#### Scenario: Wrong role through Scalar
- **WHEN** a developer enters the seeded Guard's token and calls a controller-only endpoint
- **THEN** the response is HTTP 403, exactly as for any other client

### Requirement: Provider key in Scalar
A developer SHALL be able to enter the provider API key in Scalar for the API-key scheme and call provider endpoints with it. Neither the reference page nor the OpenAPI document SHALL contain a token or API key pre-filled by the application.

#### Scenario: Provider call
- **WHEN** a developer enters a valid provider key in Scalar and lists stations
- **THEN** the call succeeds with HTTP 200

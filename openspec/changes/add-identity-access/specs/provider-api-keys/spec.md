# Spec Delta

## Purpose

Lets the external catering provider, a machine client, authenticate with a narrow, revocable API key that grants access to provider endpoints only (CAT-01, EP-12, EP-15).

## ADDED Requirements

### Requirement: API key authentication on provider endpoints
Provider endpoints SHALL require an `X-Api-Key` header holding an active key. A missing, unknown or revoked key SHALL be refused with HTTP 401 (CAT-01).

#### Scenario: Valid key
- **WHEN** a request to a provider endpoint carries the seeded provider's active key
- **THEN** the request is served as that provider

#### Scenario: Missing key
- **WHEN** a request to a provider endpoint carries no `X-Api-Key` header
- **THEN** the response is HTTP 401

#### Scenario: Revoked key
- **WHEN** the key's revocation time is set and the key is used
- **THEN** the response is HTTP 401

### Requirement: Keys stored only as hashes
The system SHALL store only a one-way hash of each API key plus a short non-secret prefix used to find it. The plaintext key SHALL be supplied through configuration at seeding and SHALL NOT be stored or logged (data-model.md, EP-15).

#### Scenario: Inspecting stored keys
- **WHEN** the `identity.provider_api_keys` table is inspected
- **THEN** it holds the prefix and the hash, and the plaintext key appears nowhere in the database or logs

### Requirement: Key comparison resists timing attacks
Key verification SHALL compare hashes in constant time, and SHALL give the same 401 response whether the prefix is unknown or the key is wrong.

#### Scenario: Wrong key with a valid prefix
- **WHEN** a key with a correct prefix but wrong secret is presented
- **THEN** the response is identical to the response for an unknown prefix

### Requirement: Provider identity available to the catering module
For every authenticated provider request the system SHALL make the provider's ID available to the handling module, so the provider sees and manages only its own subscriptions (CAT-04).

#### Scenario: Provider-scoped request
- **WHEN** an authenticated provider calls a provider endpoint
- **THEN** the handling module can read that provider's ID

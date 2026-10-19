# Spec Delta

## Purpose

Lets the provider tell SmartRail where to push its changes, and keeps that registration safe, because SmartRail calls out to an address the provider controls (CAT-04).

## ADDED Requirements

### Requirement: Register a callback URL
An authenticated provider SHALL be able to register one callback URL, receiving a subscription ID; registering again SHALL replace its existing URL rather than add a second (CAT-04).

#### Scenario: First registration
- **WHEN** the provider registers `https://rail-bites.example/hooks/smartrail`
- **THEN** the response is 201 with the subscription ID and the subscription is active

#### Scenario: Replace
- **WHEN** the provider registers a new URL
- **THEN** the old URL receives no further deliveries

### Requirement: URL restrictions
A callback URL SHALL be an absolute `https` URL of at most 2,048 characters, SHALL NOT include credentials, and in a production environment SHALL NOT resolve to a loopback, link-local or private address; `http` and private addresses SHALL be accepted only when explicitly allowed by configuration for local development (EP-15 server-side request risk).

#### Scenario: Plain http in production
- **WHEN** the provider registers an `http://` URL in a production configuration
- **THEN** the response is HTTP 400

#### Scenario: Private address
- **WHEN** the URL resolves to 10.0.0.5 in a production configuration
- **THEN** the response is HTTP 400

#### Scenario: Local development
- **WHEN** the development configuration allows private addresses
- **THEN** the simulated provider's `http://` address is accepted

### Requirement: Manage own subscription
A provider SHALL be able to read and deactivate its own subscription and SHALL NOT see or alter another provider's (CAT-04, least privilege).

#### Scenario: Deactivate
- **WHEN** the provider deactivates its subscription
- **THEN** no further deliveries are created for it

### Requirement: Events from registration onward
A subscription SHALL receive events published after it became active; it SHALL NOT receive earlier events automatically, and the provider SHALL use the journeys endpoint to learn current state (CAT-03).

#### Scenario: Late registration
- **WHEN** a provider registers after a delay was published
- **THEN** it receives later changes only, and reads the current state from the journeys endpoint

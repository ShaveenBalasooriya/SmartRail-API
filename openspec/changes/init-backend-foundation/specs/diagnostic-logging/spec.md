# Spec Delta

## Purpose

Gives operators structured diagnostic logs to run the platform in production (brief §19, EP-17), while guaranteeing that secrets and passenger personal data never reach them (EP-14, S-20).

## ADDED Requirements

### Requirement: Structured request logging
The system SHALL write one structured (JSON) log entry for every completed HTTP request, containing the timestamp, HTTP method, route template, response status, duration in milliseconds and correlation ID (EP-17).

#### Scenario: Request completes
- **WHEN** a request to `GET /api/v1/stations` completes with HTTP 200
- **THEN** a JSON log entry records the method, the route template, status 200, the duration and the correlation ID

#### Scenario: Route with identifiers
- **WHEN** a request to `/api/v1/journeys/{journeyId}` completes
- **THEN** the entry records the route template `/api/v1/journeys/{journeyId}`, not the raw path with its query string

### Requirement: Secrets never logged
The system SHALL NOT write bearer tokens, `Authorization` header values, API keys, cookies or connection strings to any log entry, at any log level (EP-15, EP-14).

#### Scenario: Authenticated request
- **WHEN** a request carrying an `Authorization: Bearer` header or an `X-Api-Key` header is logged
- **THEN** no log entry contains the token or the key

### Requirement: No passenger personal data or client IP in logs
The system SHALL NOT write passenger personal data or client IP addresses to any log entry (EP-14, S-20, M-03).

#### Scenario: Public request from a passenger
- **WHEN** an anonymous passenger request is logged
- **THEN** the entry contains no client IP address and no user-identifying value

#### Scenario: Automated check
- **WHEN** the test suite runs the PII log test against a captured log of representative requests
- **THEN** the test fails if any entry contains a configured sensitive value (a token, an API key or an IP address)

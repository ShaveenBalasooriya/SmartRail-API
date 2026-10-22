# Spec Delta

## Purpose

Defines the HTTP contract every SmartRail API endpoint shares (base path, JSON shape, timestamps, errors and correlation), so every client, test and integration can rely on one consistent behaviour.

## ADDED Requirements

### Requirement: Versioned base path
Every application endpoint SHALL be served under the base path `/api/v1`. The only exceptions are the health endpoints, which are served under `/health`.

#### Scenario: Unknown route
- **WHEN** a client requests a path under `/api/v1` that matches no endpoint
- **THEN** the response is HTTP 404 in the standard error format

### Requirement: JSON format
Request and response bodies SHALL be JSON with camelCase property names, and enumeration values SHALL be serialised as lower-case snake_case strings rather than numbers.

#### Scenario: Enumeration in a response
- **WHEN** an endpoint returns a journey whose lifecycle status is cancelled
- **THEN** the JSON property `lifecycleStatus` has the string value `cancelled`

#### Scenario: Unsupported media type
- **WHEN** a client sends a request body with a content type other than `application/json` to an endpoint that accepts JSON
- **THEN** the response is HTTP 415 in the standard error format

### Requirement: Timestamp format
Every timestamp the API returns SHALL be an ISO 8601 UTC instant ending in `Z`. Every timestamp the API accepts SHALL include a UTC offset, and a timestamp without one SHALL be rejected (M-02 freshness depends on unambiguous times).

#### Scenario: Timestamp without an offset
- **WHEN** a client submits a timestamp `2026-10-19T08:15:00` with no offset
- **THEN** the response is HTTP 400 identifying that field as invalid

#### Scenario: Timestamp in a response
- **WHEN** any endpoint returns a time
- **THEN** it is formatted as, for example, `2026-10-19T02:45:00Z`

### Requirement: Standard error format
Every error response SHALL use the RFC 9457 problem-details format (`application/problem+json`), with `type`, `title`, `status` and a `traceId`. It SHALL NOT include stack traces, exception messages or internal identifiers outside the Development environment (EP-15).

#### Scenario: Unhandled server error
- **WHEN** an endpoint throws an unexpected exception in a non-development environment
- **THEN** the response is HTTP 500 problem details with a generic title and a `traceId`, and contains no exception text

#### Scenario: Error in Development
- **WHEN** the same exception occurs in the Development environment
- **THEN** the problem details may add the exception detail, to help debugging

### Requirement: Validation errors
A request that fails input validation SHALL be rejected with HTTP 400 problem details that include an `errors` object mapping each invalid field to its messages, and SHALL cause no state change (EP-15 input validation).

#### Scenario: Several invalid fields
- **WHEN** a request has two invalid fields
- **THEN** the response is HTTP 400 and `errors` lists both fields

#### Scenario: Malformed JSON
- **WHEN** a request body is not valid JSON
- **THEN** the response is HTTP 400 problem details and nothing is changed

### Requirement: Correlation ID
Every response SHALL carry an `X-Correlation-Id` header. When the request supplied a well-formed `X-Correlation-Id` (up to 64 characters of letters, digits and hyphens), the response SHALL echo it; otherwise the system SHALL generate one. The same value SHALL appear in that request's log entries.

#### Scenario: Caller supplies an ID
- **WHEN** a request carries `X-Correlation-Id: abc-123`
- **THEN** the response carries `X-Correlation-Id: abc-123` and the request's log entries include `abc-123`

#### Scenario: Caller supplies an invalid ID
- **WHEN** a request carries an `X-Correlation-Id` longer than 64 characters or with other characters
- **THEN** the system ignores it and returns a newly generated ID

### Requirement: Browser origins
The API SHALL accept cross-origin browser requests only from origins listed in configuration (the operations web, the passenger web build and the station display), and SHALL reject all other origins.

#### Scenario: Unlisted origin
- **WHEN** a browser preflight request arrives from an origin that is not configured
- **THEN** the response carries no `Access-Control-Allow-Origin` header for that origin

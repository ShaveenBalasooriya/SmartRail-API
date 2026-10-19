# Spec Delta

## Purpose

Protects the open public endpoints and the provider API from being degraded by one client, as required by the EP-15 security controls and CAT-11.

## ADDED Requirements

### Requirement: Public endpoint rate limit
Requests to public endpoints SHALL be limited per client to a configured number per minute (default 120). A request over the limit SHALL be refused with HTTP 429 problem details and a `Retry-After` header (EP-15).

#### Scenario: Under the limit
- **WHEN** a client makes 100 public requests within one minute
- **THEN** every request is served

#### Scenario: Over the limit
- **WHEN** a client makes its 121st public request within one minute
- **THEN** the response is HTTP 429 with a `Retry-After` header

### Requirement: Provider rate limit per key
Requests to provider endpoints SHALL be limited per API key to a configured number per minute (default 60), refused with HTTP 429 and `Retry-After` when exceeded (CAT-11).

#### Scenario: Provider exceeds its limit
- **WHEN** the provider makes its 61st request within one minute
- **THEN** the response is HTTP 429 and no data is returned

### Requirement: Live streams count once per connection
Opening a live update stream SHALL count as one request against the limit; the events delivered on an open stream SHALL NOT count.

#### Scenario: Long-lived stream
- **WHEN** a display keeps one stream open for an hour and receives many events
- **THEN** the stream is not cut off by the rate limit

### Requirement: Staff and health endpoints are not rate limited
Authenticated staff endpoints and health endpoints SHALL NOT be rate limited in MVP 1, so that operations are never throttled during a disruption.

#### Scenario: Controller under pressure
- **WHEN** a Network Controller makes 200 requests in a minute
- **THEN** none is refused with 429

### Requirement: Rate limiting keeps no client IP in logs
Partitioning public clients for rate limiting SHALL NOT cause the client IP address to be written to logs or persisted (EP-14).

#### Scenario: Limited request logged
- **WHEN** a public request is refused with 429
- **THEN** its log entry has the status 429 and no IP address

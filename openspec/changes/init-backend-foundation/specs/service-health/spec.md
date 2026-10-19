# Spec Delta

## Purpose

Lets the container runtime and operators tell whether the SmartRail API process is alive and whether it can actually serve requests, so a broken instance is detected rather than silently failing (EP-17).

## ADDED Requirements

### Requirement: Liveness endpoint
The system SHALL expose `GET /health/live`, which returns HTTP 200 whenever the API process is running and able to respond, without checking any dependency (EP-17).

#### Scenario: Process is running
- **WHEN** a client calls `GET /health/live`
- **THEN** the response is HTTP 200 with a body reporting the status `Healthy`

#### Scenario: Database is down
- **WHEN** PostgreSQL is unreachable and a client calls `GET /health/live`
- **THEN** the response is still HTTP 200, because liveness does not depend on the database

### Requirement: Readiness endpoint
The system SHALL expose `GET /health/ready`. It returns HTTP 200 only when the API can reach its PostgreSQL database and every module's migrations have been applied; otherwise it returns HTTP 503 (EP-17).

#### Scenario: Ready to serve
- **WHEN** the database is reachable, migrations are applied, and a client calls `GET /health/ready`
- **THEN** the response is HTTP 200 with a body listing each check as `Healthy`

#### Scenario: Database unreachable
- **WHEN** PostgreSQL cannot be reached and a client calls `GET /health/ready`
- **THEN** the response is HTTP 503 and the body names the database check as `Unhealthy`

### Requirement: Health responses reveal no internals
Health endpoint responses SHALL NOT include connection strings, host names, credentials, exception messages or stack traces (EP-15).

#### Scenario: Failing check
- **WHEN** the readiness database check fails with an exception
- **THEN** the response body gives only the check name and its status, and the exception detail is written only to the diagnostic log

### Requirement: Health endpoints need no credentials
`/health/live` and `/health/ready` SHALL be callable without authentication, so the container runtime can probe them.

#### Scenario: Unauthenticated probe
- **WHEN** a request without any credentials calls `GET /health/ready`
- **THEN** the endpoint answers normally rather than returning 401

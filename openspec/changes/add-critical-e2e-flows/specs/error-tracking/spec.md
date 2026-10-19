# Spec Delta

## Purpose

Lets operators see unhandled failures in production without leaking secrets or personal data (EP-17).

## ADDED Requirements

### Requirement: Error tracking
Every unhandled exception SHALL be reported to the configured error-tracking service with its correlation ID and with default personal-data collection disabled. When no error-tracking service is configured, the system SHALL keep running and only log locally (EP-17).

#### Scenario: Unhandled exception
- **WHEN** an endpoint throws an unhandled exception
- **THEN** one error report is sent with the correlation ID and the route template, and without request headers, body or client IP

#### Scenario: Error tracking not configured
- **WHEN** the application starts without error-tracking configuration
- **THEN** it starts normally and writes errors to the local log only

### Requirement: Secrets and personal data never reported
Error reports SHALL NOT contain bearer tokens, `Authorization` header values, API keys, cookies, connection strings, passenger personal data or client IP addresses (EP-14, EP-15, S-20, M-03).

#### Scenario: Authenticated request fails
- **WHEN** a request carrying a bearer token and an API key throws an unhandled exception
- **THEN** the error report contains neither value

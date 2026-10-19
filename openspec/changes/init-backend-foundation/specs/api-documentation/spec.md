# Spec Delta

## Purpose

Publishes an always-accurate OpenAPI description of the SmartRail API and an interactive reference for exploring and trying it, so developers, client builders and reviewers can see and call every endpoint without reading the code.

## ADDED Requirements

### Requirement: OpenAPI document
The system SHALL generate an OpenAPI document describing every application endpoint under `/api/v1`, with its parameters, request and response bodies, status codes and problem-details errors, served at `/openapi/v1.json`.

#### Scenario: Document served
- **WHEN** a client requests `/openapi/v1.json` in an environment where documentation is enabled
- **THEN** the response is a valid OpenAPI document that lists the application endpoints

#### Scenario: Error responses described
- **WHEN** the document is inspected for an endpoint that validates input
- **THEN** it lists the 400 problem-details response

### Requirement: Scalar API reference
The system SHALL serve the Scalar interactive API reference, built from the OpenAPI document, at `/scalar`. Swagger UI SHALL NOT be used.

#### Scenario: Reference opens
- **WHEN** a developer opens `/scalar` in an environment where documentation is enabled
- **THEN** the reference lists the endpoints grouped by module and lets the developer send requests

### Requirement: Endpoints grouped and described
Each endpoint SHALL carry a summary, a tag naming its module area (for example `Guard`, `Operations`, `Public`, `Provider`, `Audit`), and a description of the access it needs (public, staff role or provider key).

#### Scenario: Access shown
- **WHEN** the reference shows the publish-delay endpoint
- **THEN** its description states that only the `network_controller` role may call it

### Requirement: Documentation environments
The OpenAPI document and the Scalar reference SHALL be enabled in the Development and End-to-end environments and disabled in any other environment unless explicitly enabled by configuration. Health endpoints and the documentation endpoints themselves SHALL need no authentication.

#### Scenario: Production
- **WHEN** the application runs in a production environment without the enabling setting
- **THEN** `/scalar` and `/openapi/v1.json` return HTTP 404

### Requirement: Document stays in step with the code
The OpenAPI document SHALL be generated from the running endpoints, and a build check SHALL fail when a mapped `/api/v1` endpoint is missing from it.

#### Scenario: Endpoint missing from the document
- **WHEN** an endpoint is mapped but excluded from the document
- **THEN** the check fails the build

# Spec Delta

## Purpose

Shows how quickly a published change reaches each channel, so the claim that a change reaches everyone promptly can be measured rather than asserted (S-19, EP-17).

## ADDED Requirements

### Requirement: Latency recorded per channel
For every pushed journey event the system SHALL record the elapsed time from the moment the change was committed to the moment it was written to each channel's connection or acknowledged by the provider, labelled by channel (`public_stream`, `guard_stream`, `provider_webhook`) and event type.

#### Scenario: Delay delivered to a stream
- **WHEN** a delay event is written to a connected public stream client
- **THEN** a latency sample for channel `public_stream` and type `delay_published` is recorded

#### Scenario: Provider delivery
- **WHEN** the provider acknowledges a webhook with 2xx
- **THEN** a sample for `provider_webhook` is recorded measuring from event commit to acknowledgement

### Requirement: Latency readable for a live change
The latency of the most recent events SHALL be readable by the operator through the metrics exporter, and SHALL be visible in the error-tracking service's performance view when configured (EP-17 Done when).

#### Scenario: Reading latency
- **WHEN** a change has just been published and delivered
- **THEN** its latency per channel can be read from the exported metrics

### Requirement: Metrics carry no personal data
Latency metrics SHALL be labelled only by channel and event type and SHALL NOT include journey, staff, subscription or client identifiers (EP-14, S-20).

#### Scenario: Label inspection
- **WHEN** the metric's labels are inspected
- **THEN** only channel and event type are present

### Requirement: Failed deliveries are not counted as latency
Deliveries that fail or are abandoned SHALL be counted as failures only (see webhook delivery) and SHALL NOT add a latency sample.

#### Scenario: Abandoned delivery
- **WHEN** a webhook delivery is abandoned
- **THEN** no latency sample is added for it

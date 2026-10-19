# Spec Delta

## Purpose

Loads the reference data MVP 1 runs on (the network, the timetable, staff accounts and the catering provider) from version-controlled files, because MVP 1 has no screens for editing any of it (mvp-1-scope.md, Owner decisions).

## ADDED Requirements

### Requirement: Seed data from repository files
Seed data SHALL be read from files kept in the repository, so that what MVP 1 runs on is reviewable and versioned. Secrets SHALL NOT be stored in those files: a secret such as the provider's API key SHALL come from configuration, and only its hash may be persisted (EP-01, EP-15).

#### Scenario: Secret supplied through configuration
- **WHEN** the seed files reference the provider API key
- **THEN** the plaintext key is read from configuration, and neither the seed files nor the database contain it

### Requirement: Idempotent seeding
Seeding SHALL be safe to run on every start. A record already present with the same natural key SHALL be left unchanged, a missing record SHALL be added, and running seeding twice SHALL leave the same data as running it once (EP-01).

#### Scenario: Second start
- **WHEN** the application starts a second time against an already seeded database
- **THEN** no duplicate rows are created and no existing row is changed

#### Scenario: New record added to a seed file
- **WHEN** a new station is added to the seed file and the application restarts
- **THEN** the new station is inserted and the existing stations are untouched

### Requirement: Seeding runs after migrations, in module order
Seeding SHALL run on application start, after every module's migrations, in a fixed order so that the data a module depends on exists first. Each module seeds only its own schema.

#### Scenario: Ordered start-up
- **WHEN** the application starts against an empty database
- **THEN** migrations run for every module, then seeding runs, and readiness reports `Healthy` only after both finish

### Requirement: Invalid seed data stops start-up
When a seed file is malformed or breaks a rule its owning module defines, the application SHALL fail to start and log which file and record are at fault, rather than start with partial data.

#### Scenario: Malformed seed file
- **WHEN** a seed file contains invalid JSON
- **THEN** the application exits with an error naming the file, and no seed data from that run is committed

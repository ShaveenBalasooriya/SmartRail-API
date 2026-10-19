# Tasks

## 1. Audit storage and chain

- [ ] 1.1 Add the `AuditEntry` domain type, canonical serialiser and hash function; verify unit tests: identical content gives identical hash, any field change changes it, genesis uses 64 zeros
- [ ] 1.2 Add the `audit.entries` migration with the append-only trigger and revoked grants; verify an integration test that UPDATE and DELETE from the application role fail
- [ ] 1.3 Add the shared request `IUnitOfWork` (one connection and transaction enlisted by every DbContext) in the Host; verify an integration test that a failure after two DbContexts write rolls both back

## 2. Recording

- [ ] 2.1 Implement `IAuditRecorder` with the advisory-locked append; verify integration tests: 20 concurrent records produce positions 1–20 with no gaps and a valid chain; a rolled-back caller leaves no entry
- [ ] 2.2 Add the `AccessDenied` handler; verify an integration test that a guard's refused write produces one `denied` entry with role and action

## 3. Auditor endpoints

- [ ] 3.1 Add the list endpoint with actor, time-range and outcome filters and paging; verify integration tests for filtering, newest-first order, invalid range → 400, controller and guard → 403
- [ ] 3.2 Add the entry-detail endpoint including `relatedEntryId`; verify an integration test with a related pair of entries
- [ ] 3.3 Add the integrity endpoint; verify integration tests: intact on an untouched record, broken at the right position after a direct SQL edit (trigger disabled in the test)
- [ ] 3.4 Add the no-passenger-data test over entries produced so far (extended by later changes); verify it passes

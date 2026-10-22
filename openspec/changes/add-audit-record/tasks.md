# Tasks

## 1. Audit storage and chain

- [ ] 1.1 Add the `AuditEntry` domain type, canonical serialiser and hash function; verify unit tests: identical content gives identical hash, any field change changes it, genesis uses 64 zeros
- [ ] 1.2 Add the `audit.entries` migration with the append-only trigger; verify a manual UPDATE on the running database is rejected
- [ ] 1.3 Add the shared request `IUnitOfWork` (one connection and transaction enlisted by every DbContext) in the Host; verify one integration test that a failure after two DbContexts write rolls both back

## 2. Recording

- [ ] 2.1 Implement `IAuditRecorder` with the advisory-locked append; verify a clean build and that two recorded entries get positions 1 and 2 with a valid chain
- [ ] 2.2 Add the `AccessDenied` handler; verify a guard's refused write produces one `denied` entry with role and action

## 3. Auditor endpoints

- [ ] 3.1 Add the list endpoint with actor, time-range and outcome filters and paging; verify by calling it as the seeded Auditor
- [ ] 3.2 Add the entry-detail endpoint including `relatedEntryId`; verify by calling it as the seeded Auditor
- [ ] 3.3 Add the integrity endpoint; verify one integration test: intact on an untouched record, broken at the right position after a direct SQL edit (trigger disabled in the test) (EP-03 Done when)

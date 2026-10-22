# Tasks

## 1. Identity data and seed

- [ ] 1.0 Add a simple seed step (moved here from `init-backend-foundation`): each module that has seed data registers a seeder, the Host runs them after migrations in module order, each seeder reads its JSON file from `seed/<module>/` and inserts only missing records by natural key, and a malformed file stops start-up naming the file; secrets are read from configuration; verify start-up twice against the same database gives no duplicates
- [ ] 1.1 Add `StaffMember` (one `StaffRole`) and `ProviderApiKey` to the Identity domain with their EF mappings and first `identity` migration; verify the migration creates both tables in the `identity` schema
- [ ] 1.2 Add the Identity seeder for `seed/identity/staff.json` and the provider key (plaintext from configuration, prefix + SHA-256 stored); verify start-up stops on a staff record with an unknown role, and the plaintext key is not in the database

## 2. Staff authentication

- [ ] 2.1 Configure JWT bearer validation (issuer, audience, JWKS, lifetime) and the claims transformation that maps `sub` to the staff row and replaces role claims; verify on the running stack that a seeded user's token is accepted and a request with no token gets 401
- [ ] 2.2 Expose `ICurrentActor` (staff ID, role) and `IStaffDirectory` (look up a staff account and its role by ID or username) in `SmartRail.IdentityAccess.Contracts`; verify a clean build

## 3. Authorisation policies and denials

- [ ] 3.1 Add `.AllowAnonymousPublic()`, `.RequireProvider()`, `.RequireStaffRole(...)` and a fallback policy that requires an authenticated staff caller; verify a clean build and that the health endpoints still answer anonymously
- [ ] 3.2 Add the 403 result handler publishing `AccessDenied` for write methods only; verify one integration test: a guard token on a controller-only POST returns 403 and publishes one notification

## 4. Provider API keys

- [ ] 4.1 Implement the provider authentication handler (prefix lookup, constant-time compare, revoked/inactive → 401) with a stub provider directory; verify the seeded key gets 200 and a wrong key gets 401 on a provider test call

## 5. Rate limiting

- [ ] 5.1 Add the `public` and `provider` rate-limit policies with problem-details 429 and `Retry-After`; verify by running the app with a low configured limit and seeing a 429

## 6. Scalar

- [ ] 6.1 Add the OpenAPI bearer and API-key security schemes and the per-endpoint requirements from policy metadata; verify in Scalar that a pasted controller token calls a staff endpoint and the provider key calls a provider endpoint

## 7. Authentik and documentation

- [ ] 7.1 Add the Authentik blueprint and mount it in `compose.yaml`; verify on a fresh stack that each seeded staff user obtains a token (client_credentials with app password) that resolves to its SmartRail role
- [ ] 7.2 Write `docs/security/threat-model.md` and `docs/security/data-classification.md`; verify each top risk names its control, and the deferred callback-address (server-side request) control is listed as a known gap

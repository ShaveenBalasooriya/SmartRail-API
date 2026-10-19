# Tasks

## 1. Identity data and seed

- [ ] 1.1 Add `StaffMember` (one `StaffRole`) and `ProviderApiKey` to the Identity domain with their EF mappings and first `identity` migration; verify the migration creates both tables in the `identity` schema with no credential columns
- [ ] 1.2 Add the Identity seeder for `seed/identity/staff.json` and the provider key (plaintext from configuration, prefix + SHA-256 stored); verify integration tests: two roles or unknown role fails start-up, re-seed creates no duplicates, plaintext key absent from the database

## 2. Staff authentication

- [ ] 2.1 Configure JWT bearer validation (issuer, audience, JWKS, lifetime) and the claims transformation that maps `sub` to the staff row and replaces role claims; verify integration tests with a test-signed token issuer: valid → authenticated, missing → 401, expired/wrong audience/bad signature → 401, unknown subject → 403, token group claim ignored
- [ ] 2.2 Expose `ICurrentActor` (staff ID, role) and `IStaffDirectory` (look up a staff account and its role by ID or username) in `SmartRail.IdentityAccess.Contracts`; verify unit tests of both

## 3. Authorisation policies and denials

- [ ] 3.1 Add `.AllowAnonymousPublic()`, `.RequireProvider()`, `.RequireStaffRole(...)` and the start-up check for unclassified endpoints; verify an integration test that an unclassified test endpoint stops start-up
- [ ] 3.2 Add the 403 result handler publishing `AccessDenied` for write methods only; verify integration tests with test endpoints: guard on a controller-only POST → 403 + one notification; guard on a controller-only GET → 403 + no notification; auditor on any POST → 403
- [ ] 3.3 Verify credential separation: integration tests that an API key on a staff endpoint and a staff token on a provider endpoint both give 401

## 4. Provider API keys

- [ ] 4.1 Implement the provider authentication handler (prefix lookup, constant-time compare, revoked/inactive → 401) with a stub provider directory; verify integration tests: valid, missing, revoked, inactive provider, wrong secret with valid prefix gives identical 401

## 5. Rate limiting

- [ ] 5.1 Add the `public` and `provider` rate-limit policies with problem-details 429 and `Retry-After`; verify integration tests: 121st public request → 429, 61st provider request → 429, staff and health endpoints never 429, the 429 log entry has no IP

## 6. Scalar sign-in

- [ ] 6.1 Add the OpenAPI OAuth2 and API-key security schemes and the per-endpoint requirements from policy metadata; verify tests that a staff endpoint requires the staff scheme, a provider endpoint the key scheme and a public endpoint none
- [ ] 6.2 Add the `smartrail-scalar` public PKCE client to the Authentik blueprint, accept both client IDs as staff audiences, and configure Scalar's authorisation-code flow and API-key entry; verify on the running stack that signing in through Scalar as the seeded Controller returns 200 on the live view and as the seeded Guard returns 403 on a controller endpoint, and that the reference page and document contain no secret

## 7. Authentik and documentation

- [ ] 7.1 Add the Authentik blueprint and mount it in `compose.yaml`; verify on a fresh stack that each seeded staff user obtains a token (client_credentials with app password) that resolves to its SmartRail role
- [ ] 7.2 Write `docs/security/threat-model.md` and `docs/security/data-classification.md`; verify each top risk names its control and the test that proves it

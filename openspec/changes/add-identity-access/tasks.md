# Tasks

## 1. Identity data and seed

- [x] 1.0 Add the seed step (design D9): `IModuleSeeder`, the Host runs seeders after migrations in module order, each reads its JSON file from `seed/<module>/` and inserts only missing records by natural key, a malformed file stops start-up naming the file, and secrets come from configuration; verify start-up twice against the same database gives no duplicates
- [x] 1.1 Add `StaffMember` and `ProviderApiKey` to the Identity domain with their EF mappings and first `identity` migration (no role column); verify the migration creates both tables in the `identity` schema
- [x] 1.2 Add the Identity seeder for `seed/identity/staff.json` and the provider key (plaintext from configuration, prefix + SHA-256 stored); verify start-up stops on a malformed staff file naming it, and the plaintext key is not in the database

## 2. Staff authentication

- [x] 2.1 Configure JWT bearer validation (D1) and the claims transformation that adds `staff_id` and resolves the role from `groups` (D2); add `GET /identity/me` (D5); verify on the running stack that a seeded user's token returns its role from `/identity/me` and a request with no token gets 401
- [x] 2.2 Add `ICurrentUser`, `Roles` and `Policies` to `SmartRail.SharedKernel.Application` and `HttpContextCurrentUser` in the Identity Api project (D3); verify a clean build and the architecture tests pass

## 3. Authorisation policies and denials

- [x] 3.1 Add `.AllowAnonymousPublic()`, `.RequireProvider()`, `.RequireStaffRole(...)` to `SmartRail.SharedKernel.Api` and a fallback policy that requires an authenticated staff caller; mark the Rail Network endpoints public; verify the health and network endpoints still answer anonymously
- [x] 3.2 Add the 403 result handler publishing `AccessDenied` for write methods only; verify one integration test with locally signed tokens (D11) against a test endpoint: a `guard` token on a controller-only POST returns 403 and publishes one notification, and a token with two role groups gets 403

## 4. Provider API keys

- [x] 4.1 Implement the provider authentication handler (D7); verify in the integration tests against a test endpoint that the seeded key gets 200, and a wrong or revoked key gets 401

## 5. Rate limiting

- [x] 5.1 Add the `public` and `provider` rate-limit policies with problem-details 429 and `Retry-After`; verify by running the app with a low configured limit and seeing a 429

## 6. Scalar

- [x] 6.1 Add the OpenAPI bearer and API-key security schemes and the per-endpoint requirements from policy metadata; verify in Scalar that a pasted staff token calls `/identity/me`, and that the network endpoints show no security requirement
- [x] 6.2 Replace the staff bearer scheme with OAuth2 authorization code + PKCE (D10): the scheme's URLs from `Authentication:Staff:AuthorizationUrl`/`TokenUrl`, Scalar configured with the staff client ID, PKCE S256 and the `openid profile offline_access` scopes; add the staff settings to `appsettings.Development.json`; accept a plain-http metadata address only in Development; verify the document's staff scheme and that the Host starts outside Compose

## 7. Authentik and documentation

- [x] 7.1 Add the Authentik blueprint with the three role groups and mount it in `compose.yaml`; verify on a fresh stack that each seeded staff user obtains a token (client_credentials with app password) whose `/identity/me` shows the matching role
- [x] 7.2 Write `docs/security/threat-model.md` and `docs/security/data-classification.md`; verify each top risk names its control, and the deferred callback-address (server-side request) control and role changes outside SmartRail's audit record are listed as known gaps
- [x] 7.3 Make the staff client public with authorization code + PKCE (D10): redirect URIs for the operations web origin and the Scalar page, `offline_access` scope, sign-in passwords for the seeded users from environment variables; keep client_credentials for automation if Authentik allows it on a public client; verify on a fresh stack that a seeded user completes a PKCE sign-in and the token's `/identity/me` shows the matching role, and that an unregistered redirect URI is refused

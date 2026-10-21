# Design: Identity and access control

## Context

Builds on `init-backend-foundation` and `refactor-module-architecture`. Architecture §5 fixes the
decisions: Authentik issues staff identity, the provider uses an API key, and passengers are anonymous
in MVP 1. The owner moved role assignment to Authentik groups (2026-10-20); SmartRail still owns the
role catalogue, the check on every endpoint and the denial event. Tables follow `../docs/data-model.md`
(`identity.staff_members`, `identity.provider_api_keys`).

## Goals / Non-Goals

**Goals:**
- One place where every endpoint's access policy is declared and enforced.
- Denials become an event that Audit can record without Identity knowing about Audit.

**Non-Goals:**
- Account management, passenger identity, token issuance (Authentik does it).

## Decisions

### D1. JWT bearer validation against Authentik's OIDC metadata
`AddAuthentication().AddJwtBearer("staff", ...)` with `Audience` = the staff client ID and clock skew
30 s. `MetadataAddress` and `ValidIssuer` are configured separately: inside Compose the API fetches the
discovery document and JWKS over the container network, while tokens carry the issuer URL the client
used. `MapInboundClaims = false`. Policies check the single role resolved in D2, never the raw
`groups` claim, so a token with two role groups cannot satisfy either. Signing keys are cached and refreshed
by the handler. *Alternative:* token introspection per request — rejected: an extra network call on
every staff request for no benefit while tokens are short-lived.

### D2. Subject mapping and roles
The Authentik OAuth2 provider uses subject mode **"Based on the User's username"**, so `sub` equals
the username and the seed file links `staff_members.authentik_subject` without knowing Authentik's
internal IDs. An `IClaimsTransformation` in the Identity Api project sends a query that looks up the
staff row by subject and adds a `staff_id` claim. A subject with no staff row gets no `staff_id` and is
refused with 403.

The role comes from the `groups` claim. Groups outside the catalogue are ignored. A token with exactly
one catalogue group has that role; a token with none, or with more than one, has no role and is
refused with 403 on every staff endpoint, so "one role per account" holds even if Authentik is
misconfigured. No cache: one indexed lookup per staff request is cheap at MVP 1 volume.

### D3. Current user as a Shared Kernel port
`ICurrentUser` (`StaffId`, `Role`) and the `Roles` and `Policies` name constants live in
`SmartRail.SharedKernel.Application/Abstractions`, so any module's handlers can attribute a change
without referencing Identity. `HttpContextCurrentUser` implements it in `SmartRail.IdentityAccess.Api`
and is registered by `AddIdentityAccessApi()`. Roles are not stored, so the `Roles` constants are the
whole catalogue.

### D4. Policies as endpoint metadata
Three extension methods on `RouteHandlerBuilder`/`RouteGroupBuilder` in `SmartRail.SharedKernel.Api`:
`.AllowAnonymousPublic()`, `.RequireProvider()`, `.RequireStaffRole(params string[] roles)` (taking the
`Roles` constants). Each sets an authorization policy plus a marker metadata item that the OpenAPI
transformer reads (D10). The authorization `FallbackPolicy` requires an authenticated staff caller, so
an endpoint that forgets its helper is closed, not open. Health and documentation endpoints are mapped
with `AllowAnonymous`; the existing Rail Network groups are marked `.AllowAnonymousPublic()`.

### D5. `GET /identity/me`
One staff endpoint (any role) returning the caller's staff ID, username and role. It is the only staff
endpoint until later changes, and it is what tasks 2.1, 6.1 and 7.1 verify against on the running app.

### D6. Denials as a MediatR notification
A custom `IAuthorizationMiddlewareResultHandler` in the Identity Api project intercepts Forbidden
results. If the endpoint's HTTP method is POST/PUT/PATCH/DELETE it publishes
`AccessDenied(StaffId?, Role?, Action, AttemptedAt)` (in `SmartRail.IdentityAccess.Contracts`), where
`Action` is the endpoint's name metadata; then writes the 403 problem response. Audit's handler
arrives in `add-audit-record`; until then the notification has no handler, which is valid.

> **Clarified 2026-10-21 (owner decision; `add-event-bus`):** `AccessDenied` is no longer a MediatR
> notification. It is published through the event bus (an outbox row in the `identity` schema, delivered
> at least once to Audit's `audit.access-denied` consumer), so a denial is not lost if the audit write
> fails. The code change is a task in `add-event-bus` (2.1); this change's history is not rewritten.

### D7. Provider API key scheme
A custom `AuthenticationHandler` for scheme "provider" in the Identity Api project, sending a query to
look up the key. Key format `srk_<8-char prefix>_<secret>`; lookup by prefix, SHA-256 over the full key
(keys are high-entropy random, so a slow password hash is unnecessary), compared with
`CryptographicOperations.FixedTimeEquals`. A missing, unknown or revoked key gives the same 401. On
success the principal carries a `provider_id` claim, which Catering reads in its own change. The seeder
reads the plaintext from configuration (`Catering__ProviderApiKey`) and stores prefix + hash. Whether
the provider is active is checked by `add-catering-integration`, which owns the provider record.

### D8. Rate limiting
`AddRateLimiter` with two fixed-window policies: `public` partitioned by remote IP (held in memory
only, never logged) and `provider` partitioned by provider ID. `OnRejected` writes problem details and
`Retry-After`.

### D9. Seed step
`IModuleSeeder` in `SmartRail.SharedKernel.Application/Abstractions`. Modules implement it in
Infrastructure; the Host runs every seeder after migrations, in module order. A seeder reads its JSON
file from `seed/<module>/`, inserts only records missing by natural key, and a malformed file stops
start-up naming the file. Only Identity seeds in this change: `seed/identity/staff.json` (subject,
username, display name — no role) and the provider key.

### D10. Authentik blueprint and Scalar
`deploy/authentik/blueprints/smartrail.yaml` defines the staff OAuth2 provider, the application, the
groups `network_controller`, `guard` and `auditor`, and the seeded staff users, each in one group.
Mounted into the Authentik server and worker in `compose.yaml`.

The staff client is **public** and signs staff in with **authorization code + PKCE (S256)**, because
the operations web (and later the Guard app) sign staff in from the browser
(`../docs/frontend-architecture.md` §4; owner decision 2026-10-21). One client serves the frontends and
Scalar, so every token has the same audience. Settings: subject mode username, issuer per provider,
`openid`, `profile` (puts `groups` in the token) and `offline_access` (refresh tokens) scopes,
access-token lifetime 15 min. Redirect URIs allow the operations web origin and the Scalar page on the
local stack. Each seeded user gets a sign-in password and an app password, both from environment
variables. The app password keeps the client_credentials grant for test automation, if Authentik allows
it on a public client; otherwise automation gets its own confidential client with the same audience.

The OpenAPI document declares the staff scheme as OAuth2 authorization code (authorization and token
URLs from `Authentication:Staff:AuthorizationUrl` and `TokenUrl`, the public Authentik URLs) and an
`X-Api-Key` scheme, through an `IOpenApiDocumentTransformer`; each endpoint's policy metadata adds the
matching requirement through an operation transformer. Scalar is configured with the staff client ID
(not a secret for a public client), PKCE S256 and the three scopes, so a developer signs in as a
seeded user from the reference page. No token or key is pre-filled.

The staff settings are also in `appsettings.Development.json` with the `localhost:9000` URLs, so the
Host runs outside Compose. The JWT handler accepts a plain-http metadata address only in Development.

### D11. Test tokens
Integration tests replace the staff scheme's signing keys and issuer with a local key through
`PostConfigure<JwtBearerOptions>` in the test factory, and mint tokens with a chosen `sub` and
`groups`. Authentik is only needed on the running stack.

### D12. Security documentation
`docs/security/threat-model.md` (STRIDE over the MVP surfaces: staff API, public API + streams,
provider API + webhooks, database, Authentik) and `docs/security/data-classification.md` (public,
operational, staff, provider-shared; what goes in logs; what each role sees). Each top risk maps to a
control and a test ID.

## Risks / Trade-offs

- [Role changes happen in Authentik, so SmartRail's audit record does not see them] → Roles are
  blueprint seed data in MVP 1; Authentik's own event log records group changes. Listed in the threat
  model.
- [Username as subject means renaming a user breaks the link] → Usernames are seed data and not
  renamed in MVP 1; revisit when account management arrives.
- [IP partitioning undercounts clients behind shared NAT, e.g. a station Wi-Fi] → Limits are
  generous; the display is one client.

## Open Questions

None that change the specs.

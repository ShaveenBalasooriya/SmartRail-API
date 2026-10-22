# Design: Identity and access control

## Context

Builds on `init-backend-foundation`. Architecture §5 fixes the decisions: Authentik issues staff
identity, SmartRail decides authorisation from its own records, the provider uses an API key, and
passengers are anonymous in MVP 1. Tables follow `../docs/data-model.md` (`identity.staff_members`,
`identity.provider_api_keys`).

## Goals / Non-Goals

**Goals:**
- One place where every endpoint's access policy is declared and enforced.
- Denials become an event that Audit can record without Identity knowing about Audit.

**Non-Goals:**
- Account management, passenger identity, token issuance (Authentik does it).

## Decisions

### D1. JWT bearer validation against Authentik's OIDC metadata
`AddAuthentication().AddJwtBearer("staff", ...)` with `Authority` = Authentik's staff application
issuer URL and `Audience` = the staff client ID; signing keys come from the discovery document's JWKS
and are cached and refreshed by the handler. Clock skew 30 s. *Alternative:* token
introspection per request — rejected: an extra network call on every staff request for no benefit
while tokens are short-lived.

### D2. Subject mapping
The Authentik OAuth2 provider is configured with subject mode **"Based on the User's username"**, so
`sub` equals the username and the seed file can link `staff_members.authentik_subject` without
knowing Authentik's internal IDs. A claims transformation looks up the staff row and adds SmartRail's own role claim, after
removing any incoming role/group claims. No cache: one indexed lookup per staff request is cheap at
MVP 1 volume.

### D3. Policies as endpoint metadata
Three extension methods on `RouteHandlerBuilder`/`RouteGroupBuilder`: `.AllowAnonymousPublic()`,
`.RequireProvider()`, `.RequireStaffRole(params StaffRole[])`. Each sets an authorization policy plus
a marker metadata item that the OpenAPI transformer reads (D7a). The authorization `FallbackPolicy`
requires an authenticated staff caller, so an endpoint that forgets its helper is closed, not open.
Health and documentation endpoints are mapped with `AllowAnonymous`.

### D4. Denials as a MediatR notification
A custom `IAuthorizationMiddlewareResultHandler` intercepts Forbidden results. If the endpoint's HTTP
method is POST/PUT/PATCH/DELETE it publishes `AccessDenied(StaffId?, Role?, Action, AttemptedAt)`
(in `SmartRail.IdentityAccess.Contracts`), where `Action` is the endpoint's name metadata; then
writes the 403 problem response. Audit's handler arrives in `add-audit-record`; until then the
notification has no handler, which is valid.

### D5. Provider API key scheme
A custom `AuthenticationHandler` for scheme "provider": key format `srk_<8-char prefix>_<secret>`; lookup by prefix, hash with SHA-256 over the full key (keys are high-entropy random, so a slow
password hash is unnecessary), compare with `CryptographicOperations.FixedTimeEquals`. The seeder
reads the plaintext from configuration (`Catering__ProviderApiKey`) and stores prefix + hash.
Provider record itself lives in Catering; Identity stores `provider_id` as a plain ID and checks the
provider is active through `ICateringProviderDirectory` (Catering contract) — implemented in
`add-catering-integration`; until then a stub returns "active" for the seeded provider.

### D6. Rate limiting
`AddRateLimiter` with two fixed-window policies: `public` partitioned by remote IP (held in memory
only, never logged) and `provider` partitioned by provider ID. `OnRejected` writes problem details
and `Retry-After`. Stream endpoints are counted at connection time because the limiter runs before
the handler.

### D7. Authentik blueprint
`deploy/authentik/blueprints/smartrail.yaml` defines: the SmartRail staff OAuth2 provider (subject
mode username, access-token lifetime 15 min, client_credentials enabled for test automation),
the application, and the seeded staff users with app passwords sourced from environment variables.
Mounted into the Authentik worker in `compose.yaml`.

### D7a. Scalar
The OpenAPI document adds an HTTP `bearer` scheme and an `X-Api-Key` scheme through an
`IOpenApiDocumentTransformer`, and each endpoint's policy metadata adds the matching requirement
through an operation transformer. The developer gets a token for a seeded user from Authentik with
the same client_credentials call the end-to-end tests use, and pastes it into Scalar.
*Alternative:* a second, public Authentik client with an authorization-code + PKCE sign-in from
Scalar. Rejected as more Authentik configuration and audience handling than a demo needs; it can be
added when there is a real browser client.

### D8. Security documentation
`docs/security/threat-model.md` (STRIDE over the MVP surfaces: staff API, public API + streams,
provider API + webhooks, database, Authentik) and `docs/security/data-classification.md` (public,
operational, staff, provider-shared; what goes in logs; what each role sees). Each top risk maps to a
control and a test ID.

## Risks / Trade-offs

- [Username as subject means renaming a user breaks the link] → Usernames are seed data and not
  renamed in MVP 1; revisit when account management arrives.
- [IP partitioning undercounts clients behind shared NAT, e.g. a station Wi-Fi] → Limits are
  generous; the display is one client.

## Open Questions

None that change the specs.

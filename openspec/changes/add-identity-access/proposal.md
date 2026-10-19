# Proposal: Identity and access control

## Why

Separation of duties is a correctness requirement of SmartRail, not a nice-to-have (brief §14,
`moscow-scope.md` M-04). Every later write — a Guard's departure log, a Network Controller's status
change — must be accepted only from the right role, enforced by the server rather than hidden in a UI.
Public passenger information must stay open with no sign-in, and the catering provider needs its own
narrow credential. This has to exist before any endpoint that changes railway state.

Delivers **EP-02** (and the authorisation, API-key handling and rate-limiting controls of **EP-15**).
Stories: NC-08, GRD-09, AUD-06, CAT-01, CAT-11, PAX-01; the authentication half of the
"Separation of duties denials" end-to-end flow.

## What Changes

- Staff authenticate with OIDC bearer tokens issued by Authentik's staff client; SmartRail validates
  them and maps the token subject to a seeded staff record.
- A fixed role catalogue (`network_controller`, `guard`, `auditor`), one role per staff account, held
  in SmartRail's `identity` schema — never read from token claims.
- Server-side authorisation on every staff endpoint: 401 without a valid token, 403 for the wrong
  role. Each module declares which role its endpoints need.
- A published "access denied" event that the Audit change records (the recording itself is in
  `add-audit-record`).
- Anonymous access for public passenger and display endpoints.
- API-key authentication for the catering provider: key hashed at rest, revocable, accepted only on
  provider endpoints.
- Rate limiting on public and provider endpoints (429 with `Retry-After`).
- Seed data: staff accounts and the provider's API key hash; Authentik blueprint with the staff client
  and the matching seeded users.
- OpenAPI security schemes so a staff bearer token or the provider key can be entered in Scalar to
  call protected endpoints.
- A basic threat model and the data-classification table (EP-14, EP-15 documentation).

## Capabilities

### New Capabilities

- `staff-authentication`: validating staff bearer tokens and resolving them to a SmartRail staff
  account.
- `role-based-access`: the role catalogue and how every endpoint is classified and enforced (staff
  role, public, provider).
- `provider-api-keys`: authenticating the catering provider by API key.
- `api-explorer-authentication`: calling protected endpoints from the Scalar API reference.
- `api-rate-limiting`: request limits on public and provider endpoints.

### Modified Capabilities

None.

## Out of Scope

- A System Admin role, account-management endpoints, deactivation, four-eyes approval, time-bound
  roles (deferred with the System Admin, mvp-1-scope.md EP-02 Out).
- Passenger accounts and the Authentik passenger client (deferred; public endpoints stay anonymous).
- Single sign-on to other organisations, MFA policy (Authentik configuration, not SmartRail
  behaviour).
- Recording denials in the audit record (`add-audit-record` subscribes to the event published here).
- Penetration testing and SIEM (EP-15 Out).

## Impact

- New: `identity` schema tables `staff_members`, `provider_api_keys`; authentication and
  authorisation middleware in the Host; `SmartRail.IdentityAccess.Contracts` (current-actor service,
  `AccessDenied` notification).
- New seed files under `seed/identity/`; Authentik blueprint under `deploy/authentik/`.
- Every later change's endpoints declare their access policy using what this change provides.

# ADR 0002: Lab vs production authentication posture

- **Status:** Accepted
- **Date:** 2026-11-09
- **Deciders:** EntitleX infosec remediation (EX-19)

## Context

EntitleX supports local labs (Keycloak + optional DevToken HMAC) and cloud IdPs (Entra / Auth0 / Cognito). Early phases allowed convenient but overly open patterns: client-supplied actors, unauthenticated vendor simulators, optional OIDC audience, and anonymous DevToken minting of any role including `PlatformAdmin`.

## Decision

### Production / non-Development

- **OIDC required** in Production (`Oidc:Enabled` + `Oidc:Authority`).
- **`Oidc:Audience` required** whenever OIDC is enabled outside Development; `ValidateAudience = true`.
- **DevToken forced off** (`Jwt:EnableDevTokenEndpoint=false`).
- **HTTPS redirection, HSTS, and baseline security headers** enabled.
- **Vite CORS origins are not registered** (SPA is not served from localhost against a non-Dev API).
- **`/metrics` requires auth** (Basic when `Metrics:Username`/`Password` are set; otherwise the JWT fallback policy).

### Development / lab

- Keycloak OIDC is the preferred path; DevToken remains available when `Jwt:EnableDevTokenEndpoint=true`.
- DevToken may mint seed roles, but **`PlatformAdmin` requires `Jwt:AllowDevPlatformAdmin=true`** (default `false`). Mis-setting `ASPNETCORE_ENVIRONMENT=Development` is an ops failure mode — Production still forces DevToken off.
- OIDC audience may be empty only in Development (Keycloak lab flexibility); prefer setting `Oidc:Audience=entitlex-api`.
- Vite CORS (`localhost:5173` / `127.0.0.1:5173`) is Development-only. Production SPA is same-origin behind nginx (see [deployment.md](../deployment.md#cors-and-spa-hosting)).
- `/metrics` stays anonymous for local Prometheus scrapes.

### Application authorization

- **Actors** (`RequestedBy` and similar) are bound from `ICurrentUser`, never from the client body.
- **Resource reads** (request / approval / saga / audit): `PlatformAdmin`, `DealDeskManager`, and `RenewalManager` may read any request; others only their own (`RequestedBy == UserId`).
- **Customer ACL**: JWT multi-value claim `customer_ids` (DevToken body field or IdP protocol mapper). `PlatformAdmin` / `DealDeskManager` may access any customer; `RenewalRepresentative` (and other non-privileged roles) may search/create only for customers listed on the claim. Empty ACL denies customer-scoped operations.
- **Entitlement search**: role policy `CanSearchEntitlements` (Rep / DealDesk / PlatformAdmin) plus per-customer ACL in `ResourceAuthorizationService`.

### Vendor simulators

- CloudShield / NetworkGuard require **`SimulatorAuth:ApiKey`** (`X-Api-Key`).
- Host processes bind to **`127.0.0.1`** only; shared labs must keep simulators on a **private network** — never publish ports publicly.
- Control-plane provider HTTP clients send the matching `Providers:*:ApiKey`.

## Consequences

- Lab demos: PlatformAdmin/DealDesk still search any customer; Reps mint DevToken with `customerIds: ["CUST-1"]` (or map Keycloak `customer_ids`).
- Compose Keycloak / weak lab secrets remain disposable lab defaults (documented, not “rotated” as production secrets).
- Keycloak lab: add a user attribute / protocol mapper emitting multi-value `customer_ids` on the access token when testing OIDC ACL paths.

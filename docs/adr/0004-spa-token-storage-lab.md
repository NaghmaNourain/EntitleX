# ADR 0004 — SPA token storage (lab vs production)

## Status

Accepted for lab / portfolio; **M1 open for production-grade BFF**.

## Context

The React SPA currently stores access tokens in `localStorage` (DevToken and OIDC user-manager defaults). Infosec finding **M1** flags XSS → token theft.

## Decision

- **Lab / local Compose:** keep bearer tokens in the browser (localStorage / oidc-client-ts) for simplicity with Vite + DevToken.
- **Staging / Production target:** introduce a Backend-for-Frontend (BFF) or httpOnly cookie session so access tokens are not script-readable; add CSP; keep short-lived access tokens.

## Consequences

- Lab demos remain easy; do not treat current SPA auth storage as production-ready.
- Follow-up work: BFF endpoints + cookie auth + CSP before internet-facing deploy.

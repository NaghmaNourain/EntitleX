# EntitleX frontend (Phase 10 + OIDC)

React + TypeScript + Vite + TanStack Query + React Router + MUI control-plane UI.

## Scripts

```bash
npm install
cp .env.example .env.local   # optional overrides
npm run dev      # http://localhost:5173 (proxies /api and /health → :5080)
npm run build
```

## Auth

| Mode | When | How |
|------|------|-----|
| **OIDC PKCE** | `VITE_OIDC_AUTHORITY` + `VITE_OIDC_CLIENT_ID` set | "Sign in with SSO" → Keycloak (or cloud IdP) |
| **DevToken** | `VITE_ENABLE_DEV_TOKEN=true` **and** API Development + `Jwt:EnableDevTokenEndpoint` | Optional role picker on the login page |

Bearer tokens are attached on authenticated API calls (`Authorization: Bearer …`). See root [README](../README.md) for Keycloak demo users and cloud env vars.

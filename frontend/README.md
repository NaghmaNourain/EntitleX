# EntitleX frontend (Phase 10 + OIDC)

React + TypeScript + Vite + TanStack Query + React Router + MUI control-plane UI.

## Scripts

```bash
npm install
cp .env.example .env.local   # optional overrides
npm run dev      # http://localhost:5173 (proxies /api and /health → :5080)
npm run build
```

## JetBrains Rider

Shared run configs live in [`.run/`](../.run/) (checked into git; `.idea/` stays local):

| Configuration | What it starts |
|---------------|----------------|
| `frontend` | `npm run dev` (Vite `:5173`) |
| `EntitleX.Api` | API launch profile `http` (`:5080`) |
| **`API + Frontend`** (default) | Both together |

In Rider: open the solution → run-configuration dropdown → pick **API + Frontend** (or **frontend** alone). The SPA is not a .NET startup project; use these npm / compound configs instead of “Set as Startup Project” on a csproj.

## Auth

| Mode | When | How |
|------|------|-----|
| **OIDC PKCE** | `VITE_OIDC_AUTHORITY` + `VITE_OIDC_CLIENT_ID` set | "Sign in with SSO" → Keycloak (or cloud IdP) |
| **DevToken** | `VITE_ENABLE_DEV_TOKEN=true` **and** API Development + `Jwt:EnableDevTokenEndpoint` | Optional role picker on the login page |

Bearer tokens are attached on authenticated API calls (`Authorization: Bearer …`). See root [README](../README.md) for Keycloak demo users and cloud env vars.

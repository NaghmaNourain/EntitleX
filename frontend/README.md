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
| **`Lab Full Stack`** (default) | Shell → `scripts/lab-full-stack.sh` (Compose + simulators + API + Vite + workers) |
| `Lab Infra (Compose)` | `scripts/lab-infra-up.sh` → `docker compose up -d` (Postgres/Kafka/Keycloak/…) |
| `frontend` | `npm run dev` (Vite `:5173`) |
| `EntitleX.Api` | API launch profile `http` (`:5080`) |
| `API + Frontend` | API + Vite only (simulators/workers not included) |

In Rider: open the solution → run-configuration dropdown → pick **Lab Full Stack**. Free host ports **5432** and **8080** first if another Compose project (`untitled-*`) owns them. One-shot lambdas are not in the compound — run them separately when needed.

## Auth

| Mode | When | How |
|------|------|-----|
| **OIDC PKCE** | `VITE_OIDC_AUTHORITY` + `VITE_OIDC_CLIENT_ID` set | "Sign in with SSO" → Keycloak (or cloud IdP) |
| **DevToken** | `VITE_ENABLE_DEV_TOKEN=true` **and** API Development + `Jwt:EnableDevTokenEndpoint` | Optional role picker on the login page |

Bearer tokens are attached on authenticated API calls (`Authorization: Bearer …`). See root [README](../README.md) for Keycloak demo users and cloud env vars.

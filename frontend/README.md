# EntitleX frontend (Phase 10)

React + TypeScript + Vite + TanStack Query + React Router + MUI control-plane UI.

## Scripts

```bash
npm install
npm run dev      # http://localhost:5173 (proxies /api and /health → :5080)
npm run build
```

## Dev login

Uses `POST /api/v1/dev/tokens` (Phase 4) when the API is in Development with `Jwt:EnableDevTokenEndpoint=true`.

/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_OIDC_AUTHORITY?: string
  readonly VITE_OIDC_CLIENT_ID?: string
  readonly VITE_OIDC_AUDIENCE?: string
  readonly VITE_ENABLE_DEV_TOKEN?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}

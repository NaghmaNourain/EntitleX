/** Frontend auth feature flags / OIDC settings (Vite env). */

function readFlag(value: string | undefined): boolean {
  if (!value) return false
  return value === '1' || value.toLowerCase() === 'true' || value.toLowerCase() === 'yes'
}

const authority = (import.meta.env.VITE_OIDC_AUTHORITY as string | undefined)?.trim() ?? ''
const clientId = (import.meta.env.VITE_OIDC_CLIENT_ID as string | undefined)?.trim() ?? ''

export const authConfig = {
  /** OIDC SPA login when Authority + ClientId are set. */
  oidcEnabled: Boolean(authority && clientId),
  authority,
  clientId,
  audience: (import.meta.env.VITE_OIDC_AUDIENCE as string | undefined)?.trim() ?? '',
  /** Optional DevToken role picker (API Development + Jwt:EnableDevTokenEndpoint). */
  enableDevToken: readFlag(import.meta.env.VITE_ENABLE_DEV_TOKEN as string | undefined),
  redirectPath: '/login/callback',
  postLogoutPath: '/login',
} as const

import { User, UserManager, WebStorageStateStore, type UserManagerSettings } from 'oidc-client-ts'
import type { AuthUser, EntitleXRole } from '../api/types'
import { authConfig } from './config'

const ENTITLEX_ROLES: EntitleXRole[] = [
  'RenewalRepresentative',
  'RenewalManager',
  'DealDeskManager',
  'PlatformAdmin',
]

let manager: UserManager | null = null

function buildSettings(): UserManagerSettings {
  const redirectUri = `${window.location.origin}${authConfig.redirectPath}`
  const postLogoutRedirectUri = `${window.location.origin}${authConfig.postLogoutPath}`

  return {
    authority: authConfig.authority,
    client_id: authConfig.clientId,
    redirect_uri: redirectUri,
    post_logout_redirect_uri: postLogoutRedirectUri,
    response_type: 'code',
    scope: 'openid profile email',
    automaticSilentRenew: true,
    userStore: new WebStorageStateStore({ store: window.sessionStorage }),
    monitorSession: false,
  }
}

export function getUserManager(): UserManager {
  if (!authConfig.oidcEnabled) {
    throw new Error('OIDC is not configured (set VITE_OIDC_AUTHORITY and VITE_OIDC_CLIENT_ID).')
  }
  if (!manager) {
    manager = new UserManager(buildSettings())
  }
  return manager
}

function decodeJwtPayload(token: string): Record<string, unknown> | null {
  try {
    const parts = token.split('.')
    if (parts.length < 2) return null
    const normalized = parts[1].replace(/-/g, '+').replace(/_/g, '/')
    const padded = normalized + '='.repeat((4 - (normalized.length % 4)) % 4)
    return JSON.parse(atob(padded)) as Record<string, unknown>
  } catch {
    return null
  }
}

function collectRoles(payload: Record<string, unknown> | null): EntitleXRole[] {
  if (!payload) return []
  const raw: string[] = []

  const rolesClaim = payload.roles
  if (typeof rolesClaim === 'string') {
    raw.push(rolesClaim)
  } else if (Array.isArray(rolesClaim)) {
    for (const r of rolesClaim) {
      if (typeof r === 'string') raw.push(r)
    }
  }

  const realmAccess = payload.realm_access
  if (realmAccess && typeof realmAccess === 'object') {
    const nested = (realmAccess as { roles?: unknown }).roles
    if (Array.isArray(nested)) {
      for (const r of nested) {
        if (typeof r === 'string') raw.push(r)
      }
    }
  }

  return ENTITLEX_ROLES.filter((role) => raw.includes(role))
}

export function authUserFromOidcUser(user: User): AuthUser | null {
  const payload = decodeJwtPayload(user.access_token)
  const roles = collectRoles(payload)
  const primary = roles[0]
  if (!primary) {
    return null
  }

  const profile = user.profile
  return {
    userId: profile.sub ?? String(payload?.sub ?? ''),
    displayName:
      (typeof profile.name === 'string' && profile.name) ||
      (typeof profile.preferred_username === 'string' && profile.preferred_username) ||
      profile.sub ||
      'OIDC user',
    email: (typeof profile.email === 'string' && profile.email) || '',
    role: primary,
    roles,
  }
}

export async function beginOidcLogin(): Promise<void> {
  await getUserManager().signinRedirect()
}

export async function completeOidcLogin(): Promise<{ token: string; user: AuthUser } | null> {
  const oidcUser = await getUserManager().signinRedirectCallback()
  const user = authUserFromOidcUser(oidcUser)
  if (!user || !oidcUser.access_token) {
    return null
  }
  return { token: oidcUser.access_token, user }
}

export async function loadStoredOidcSession(): Promise<{ token: string; user: AuthUser } | null> {
  if (!authConfig.oidcEnabled) return null
  const oidcUser = await getUserManager().getUser()
  if (!oidcUser || oidcUser.expired || !oidcUser.access_token) {
    return null
  }
  const user = authUserFromOidcUser(oidcUser)
  if (!user) return null
  return { token: oidcUser.access_token, user }
}

export async function oidcLogout(): Promise<void> {
  if (!authConfig.oidcEnabled) return
  const um = getUserManager()
  const existing = await um.getUser()
  if (existing) {
    await um.signoutRedirect()
    return
  }
  await um.removeUser()
}

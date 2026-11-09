import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react'
import { configureApiClient } from '../api/client'
import { api } from '../api/endpoints'
import type { AuthUser, EntitleXRole } from '../api/types'
import { authConfig } from './config'
import {
  beginOidcLogin,
  completeOidcLogin,
  loadStoredOidcSession,
  oidcLogout,
} from './oidc'

const TOKEN_KEY = 'entitlex.accessToken'
const USER_KEY = 'entitlex.user'
const AUTH_SOURCE_KEY = 'entitlex.authSource'

type AuthSource = 'oidc' | 'devtoken'

interface AuthState {
  token: string | null
  user: AuthUser | null
  isAuthenticated: boolean
  isLoading: boolean
  oidcEnabled: boolean
  enableDevToken: boolean
  loginWithOidc: () => Promise<void>
  completeOidcCallback: () => Promise<void>
  loginWithRole: (role: EntitleXRole) => Promise<void>
  logout: () => Promise<void>
  hasRole: (...roles: EntitleXRole[]) => boolean
}

const AuthContext = createContext<AuthState | null>(null)

/** Prefer sessionStorage over localStorage to reduce XSS persistence of bearer tokens (M3). */
const sessionStore: Storage | null =
  typeof window !== 'undefined' ? window.sessionStorage : null

function readStoredUser(): AuthUser | null {
  try {
    const raw = sessionStore?.getItem(USER_KEY) ?? null
    return raw ? (JSON.parse(raw) as AuthUser) : null
  } catch {
    return null
  }
}

function persistSession(token: string, user: AuthUser, source: AuthSource) {
  sessionStore?.setItem(TOKEN_KEY, token)
  sessionStore?.setItem(USER_KEY, JSON.stringify(user))
  sessionStore?.setItem(AUTH_SOURCE_KEY, source)
  // Clear any legacy localStorage session left from earlier builds.
  localStorage.removeItem(TOKEN_KEY)
  localStorage.removeItem(USER_KEY)
  localStorage.removeItem(AUTH_SOURCE_KEY)
}

function clearSession() {
  sessionStore?.removeItem(TOKEN_KEY)
  sessionStore?.removeItem(USER_KEY)
  sessionStore?.removeItem(AUTH_SOURCE_KEY)
  localStorage.removeItem(TOKEN_KEY)
  localStorage.removeItem(USER_KEY)
  localStorage.removeItem(AUTH_SOURCE_KEY)
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [token, setToken] = useState<string | null>(() => sessionStore?.getItem(TOKEN_KEY) ?? null)
  const [user, setUser] = useState<AuthUser | null>(() => readStoredUser())
  const [isLoading, setIsLoading] = useState(true)

  useEffect(() => {
    configureApiClient(() => token)
  }, [token])

  useEffect(() => {
    let cancelled = false
    ;(async () => {
      try {
        if (!authConfig.oidcEnabled) {
          return
        }
        const session = await loadStoredOidcSession()
        if (cancelled || !session) {
          return
        }
        persistSession(session.token, session.user, 'oidc')
        setToken(session.token)
        setUser(session.user)
      } catch {
        // Stale OIDC state — fall back to whatever is in sessionStorage (may be DevToken).
      } finally {
        if (!cancelled) {
          setIsLoading(false)
        }
      }
    })()
    return () => {
      cancelled = true
    }
  }, [])

  // When OIDC is disabled, finish loading immediately.
  useEffect(() => {
    if (!authConfig.oidcEnabled) {
      setIsLoading(false)
    }
  }, [])

  const loginWithOidc = useCallback(async () => {
    await beginOidcLogin()
  }, [])

  const completeOidcCallback = useCallback(async () => {
    const session = await completeOidcLogin()
    if (!session) {
      throw new Error(
        'OIDC login succeeded but no EntitleX role was found on the access token. Check Keycloak realm roles / RoleClaimType mapping.',
      )
    }
    persistSession(session.token, session.user, 'oidc')
    setToken(session.token)
    setUser(session.user)
  }, [])

  const loginWithRole = useCallback(async (role: EntitleXRole) => {
    if (!authConfig.enableDevToken) {
      throw new Error('DevToken login is disabled (set VITE_ENABLE_DEV_TOKEN=true).')
    }
    const response = await api.mintDevToken(role)
    const nextUser: AuthUser = {
      userId: response.user.userId,
      displayName: response.user.displayName,
      email: response.user.email,
      role: response.user.role,
      roles: [response.user.role],
    }
    persistSession(response.accessToken, nextUser, 'devtoken')
    setToken(response.accessToken)
    setUser(nextUser)
  }, [])

  const logout = useCallback(async () => {
    const source = localStorage.getItem(AUTH_SOURCE_KEY) as AuthSource | null
    clearSession()
    setToken(null)
    setUser(null)
    if (source === 'oidc' && authConfig.oidcEnabled) {
      try {
        await oidcLogout()
        return
      } catch {
        // Local clear already done.
      }
    }
  }, [])

  const hasRole = useCallback(
    (...roles: EntitleXRole[]) => {
      if (!user) return false
      const granted = user.roles?.length ? user.roles : [user.role]
      return roles.some((r) => granted.includes(r))
    },
    [user],
  )

  const value = useMemo<AuthState>(
    () => ({
      token,
      user,
      isAuthenticated: Boolean(token && user),
      isLoading,
      oidcEnabled: authConfig.oidcEnabled,
      enableDevToken: authConfig.enableDevToken,
      loginWithOidc,
      completeOidcCallback,
      loginWithRole,
      logout,
      hasRole,
    }),
    [
      token,
      user,
      isLoading,
      loginWithOidc,
      completeOidcCallback,
      loginWithRole,
      logout,
      hasRole,
    ],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext)
  if (!ctx) {
    throw new Error('useAuth must be used within AuthProvider')
  }
  return ctx
}

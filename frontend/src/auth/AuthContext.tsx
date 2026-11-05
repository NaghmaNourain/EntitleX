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

const TOKEN_KEY = 'entitlex.accessToken'
const USER_KEY = 'entitlex.user'

interface AuthState {
  token: string | null
  user: AuthUser | null
  isAuthenticated: boolean
  loginWithRole: (role: EntitleXRole) => Promise<void>
  logout: () => void
  hasRole: (...roles: EntitleXRole[]) => boolean
}

const AuthContext = createContext<AuthState | null>(null)

function readStoredUser(): AuthUser | null {
  try {
    const raw = localStorage.getItem(USER_KEY)
    return raw ? (JSON.parse(raw) as AuthUser) : null
  } catch {
    return null
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [token, setToken] = useState<string | null>(() => localStorage.getItem(TOKEN_KEY))
  const [user, setUser] = useState<AuthUser | null>(() => readStoredUser())

  useEffect(() => {
    configureApiClient(() => token)
  }, [token])

  const loginWithRole = useCallback(async (role: EntitleXRole) => {
    const response = await api.mintDevToken(role)
    const nextUser: AuthUser = {
      userId: response.user.userId,
      displayName: response.user.displayName,
      email: response.user.email,
      role: response.user.role,
    }
    localStorage.setItem(TOKEN_KEY, response.accessToken)
    localStorage.setItem(USER_KEY, JSON.stringify(nextUser))
    setToken(response.accessToken)
    setUser(nextUser)
  }, [])

  const logout = useCallback(() => {
    localStorage.removeItem(TOKEN_KEY)
    localStorage.removeItem(USER_KEY)
    setToken(null)
    setUser(null)
  }, [])

  const hasRole = useCallback(
    (...roles: EntitleXRole[]) => (user ? roles.includes(user.role) : false),
    [user],
  )

  const value = useMemo<AuthState>(
    () => ({
      token,
      user,
      isAuthenticated: Boolean(token && user),
      loginWithRole,
      logout,
      hasRole,
    }),
    [token, user, loginWithRole, logout, hasRole],
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

import { Navigate, useLocation } from 'react-router-dom'
import { useAuth } from './AuthContext'
import type { EntitleXRole } from '../api/types'

export function RequireAuth({
  children,
  roles,
}: {
  children: React.ReactNode
  roles?: EntitleXRole[]
}) {
  const { isAuthenticated, hasRole } = useAuth()
  const location = useLocation()

  if (!isAuthenticated) {
    return <Navigate to="/login" replace state={{ from: location.pathname }} />
  }

  if (roles && !hasRole(...roles)) {
    return <Navigate to="/" replace />
  }

  return children
}

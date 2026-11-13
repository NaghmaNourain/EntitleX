import type { ReactNode } from 'react'
import { Navigate, useLocation } from 'react-router-dom'
import { LoadingState } from '../components/LoadingState'
import { useAuth } from './AuthContext'
import type { EntitleXRole } from '../api/types'

export function RequireAuth({
  children,
  roles,
}: {
  children: ReactNode
  roles?: EntitleXRole[]
}) {
  const { isAuthenticated, isLoading, hasRole } = useAuth()
  const location = useLocation()

  if (isLoading) {
    return <LoadingState label="Checking session…" />
  }

  if (!isAuthenticated) {
    return <Navigate to="/login" replace state={{ from: location.pathname }} />
  }

  if (roles && !hasRole(...roles)) {
    return <Navigate to="/" replace />
  }

  return children
}

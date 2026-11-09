import Box from '@mui/material/Box'
import CircularProgress from '@mui/material/CircularProgress'
import Typography from '@mui/material/Typography'
import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { ErrorAlert } from '../components/ErrorAlert'

export function OidcCallbackPage() {
  const { completeOidcCallback, isAuthenticated } = useAuth()
  const navigate = useNavigate()
  const [error, setError] = useState<unknown>(null)

  useEffect(() => {
    let cancelled = false
    ;(async () => {
      try {
        await completeOidcCallback()
        if (!cancelled) {
          navigate('/', { replace: true })
        }
      } catch (err) {
        if (!cancelled) {
          setError(err)
        }
      }
    })()
    return () => {
      cancelled = true
    }
  }, [completeOidcCallback, navigate])

  useEffect(() => {
    if (isAuthenticated) {
      navigate('/', { replace: true })
    }
  }, [isAuthenticated, navigate])

  return (
    <Box
      sx={{
        minHeight: '100vh',
        display: 'grid',
        placeItems: 'center',
        px: 2,
      }}
    >
      <Box sx={{ maxWidth: 480, width: '100%', textAlign: 'center' }}>
        {!error && (
          <>
            <CircularProgress sx={{ mb: 2 }} />
            <Typography>Completing sign-in…</Typography>
          </>
        )}
        <ErrorAlert error={error} title="OIDC callback failed" />
      </Box>
    </Box>
  )
}

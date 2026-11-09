import Alert from '@mui/material/Alert'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import Divider from '@mui/material/Divider'
import FormControl from '@mui/material/FormControl'
import FormHelperText from '@mui/material/FormHelperText'
import InputLabel from '@mui/material/InputLabel'
import MenuItem from '@mui/material/MenuItem'
import Paper from '@mui/material/Paper'
import Select from '@mui/material/Select'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { useMutation } from '@tanstack/react-query'
import { useState } from 'react'
import { Navigate, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { ErrorAlert } from '../components/ErrorAlert'
import type { EntitleXRole } from '../api/types'

const ROLES: EntitleXRole[] = [
  'RenewalRepresentative',
  'RenewalManager',
  'DealDeskManager',
  'PlatformAdmin',
]

export function LoginPage() {
  const { isAuthenticated, isLoading, oidcEnabled, enableDevToken, loginWithOidc, loginWithRole } =
    useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const from = (location.state as { from?: string } | null)?.from ?? '/'
  const [role, setRole] = useState<EntitleXRole>('RenewalRepresentative')

  const oidcMutation = useMutation({
    mutationFn: () => loginWithOidc(),
  })

  const devMutation = useMutation({
    mutationFn: () => loginWithRole(role),
    onSuccess: () => navigate(from, { replace: true }),
  })

  if (!isLoading && isAuthenticated) {
    return <Navigate to="/" replace />
  }

  const noLoginMethods = !oidcEnabled && !enableDevToken

  return (
    <Box
      sx={{
        minHeight: '100vh',
        display: 'grid',
        placeItems: 'center',
        px: 2,
        background:
          'linear-gradient(145deg, #0C2C24 0%, #1B3A4B 45%, #0F6E56 100%)',
      }}
    >
      <Paper
        elevation={0}
        sx={{
          width: '100%',
          maxWidth: 440,
          p: { xs: 3, sm: 4 },
          borderRadius: 3,
          border: '1px solid rgba(255,255,255,0.2)',
        }}
        component="main"
        aria-labelledby="login-title"
      >
        <Typography
          id="login-title"
          component="h1"
          variant="h3"
          sx={{ mb: 1, color: 'primary.dark', fontSize: { xs: '2rem', sm: '2.4rem' } }}
        >
          EntitleX
        </Typography>
        <Typography color="text.secondary" sx={{ mb: 3 }}>
          Sign in with your identity provider
          {enableDevToken ? ', or use a Development DevToken.' : '.'}
        </Typography>

        <Stack spacing={2.5}>
          {oidcEnabled && (
            <>
              <Button
                variant="contained"
                size="large"
                disabled={oidcMutation.isPending || isLoading}
                aria-busy={oidcMutation.isPending}
                onClick={() => oidcMutation.mutate()}
              >
                {oidcMutation.isPending ? 'Redirecting…' : 'Sign in with SSO'}
              </Button>
              <ErrorAlert error={oidcMutation.error} title="SSO login failed" />
              <Typography variant="body2" color="text.secondary">
                Local Keycloak users use password <code>demo</code> (lab only).
              </Typography>
            </>
          )}

          {oidcEnabled && enableDevToken && <Divider>or</Divider>}

          {enableDevToken && (
            <Stack
              spacing={2.5}
              component="form"
              onSubmit={(e) => {
                e.preventDefault()
                devMutation.mutate()
              }}
            >
              <Alert severity="info">
                DevToken panel is shown when <code>VITE_ENABLE_DEV_TOKEN=true</code> and the API
                runs in Development with <code>Jwt:EnableDevTokenEndpoint</code>.
              </Alert>

              <FormControl fullWidth required>
                <InputLabel id="role-label">Role</InputLabel>
                <Select
                  labelId="role-label"
                  label="Role"
                  value={role}
                  onChange={(e) => setRole(e.target.value as EntitleXRole)}
                >
                  {ROLES.map((r) => (
                    <MenuItem key={r} value={r}>
                      {r}
                    </MenuItem>
                  ))}
                </Select>
                <FormHelperText>Maps to seeded local users for RBAC testing</FormHelperText>
              </FormControl>

              <ErrorAlert error={devMutation.error} title="DevToken login failed" />

              <Button
                type="submit"
                variant="outlined"
                size="large"
                disabled={devMutation.isPending}
                aria-busy={devMutation.isPending}
              >
                {devMutation.isPending ? 'Signing in…' : 'Continue with DevToken'}
              </Button>
            </Stack>
          )}

          {noLoginMethods && (
            <Alert severity="warning">
              No login methods configured. Set <code>VITE_OIDC_AUTHORITY</code> /{' '}
              <code>VITE_OIDC_CLIENT_ID</code> for SSO, or <code>VITE_ENABLE_DEV_TOKEN=true</code>{' '}
              for Development tokens.
            </Alert>
          )}
        </Stack>
      </Paper>
    </Box>
  )
}

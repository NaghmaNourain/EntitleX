import Alert from '@mui/material/Alert'
import Button from '@mui/material/Button'
import Stack from '@mui/material/Stack'
import TextField from '@mui/material/TextField'
import Typography from '@mui/material/Typography'
import { useMutation } from '@tanstack/react-query'
import { useMemo, useState, type FormEvent } from 'react'
import { Link as RouterLink, useNavigate } from 'react-router-dom'
import { api } from '../api/endpoints'
import { useAuth } from '../auth/AuthContext'
import { ErrorAlert } from '../components/ErrorAlert'
import { PageHeader } from '../components/PageHeader'
import { useSelectionCart } from '../hooks/useSelectionCart'

function addDaysIso(iso: string, days: number): string {
  const d = new Date(iso)
  d.setUTCDate(d.getUTCDate() + days)
  return d.toISOString()
}

export function CreateExtensionRequestPage() {
  const { hasRole } = useAuth()
  const cart = useSelectionCart()
  const navigate = useNavigate()
  const [extensionDays, setExtensionDays] = useState(30)
  const [extensionNumber, setExtensionNumber] = useState('')
  const canCreate = hasRole('RenewalRepresentative', 'PlatformAdmin')

  const preview = useMemo(
    () =>
      cart.items.map((item) => ({
        ...item,
        requestedExpiryUtc: addDaysIso(item.expiryUtc, extensionDays),
      })),
    [cart.items, extensionDays],
  )

  const mutation = useMutation({
    mutationFn: () =>
      api.createLicenseExtensionRequest({
        extensionNumber: extensionNumber.trim() || null,
        items: preview.map((item) => ({
          entitlementId: item.entitlementId,
          provider: item.provider,
          currentExpiryUtc: item.expiryUtc,
          requestedExpiryUtc: item.requestedExpiryUtc,
        })),
      }),
    onSuccess: (created) => {
      cart.clear()
      navigate(`/requests/${created.id}`)
    },
  })

  function onSubmit(event: FormEvent) {
    event.preventDefault()
    if (!canCreate || cart.count === 0) return
    if (extensionDays < 1 || extensionDays > 3650) return
    mutation.mutate()
  }

  return (
    <>
      <PageHeader
        title="Create extension request"
        subtitle="Builds a LicenseExtensionRequest (≤1000 items) and opens the approval/saga path."
        actions={
          <Button component={RouterLink} to="/extensions/bulk" variant="outlined">
            Edit selection
          </Button>
        }
      />

      {!canCreate && (
        <Alert severity="warning" sx={{ mb: 2 }}>
          Creating requests requires RenewalRepresentative or PlatformAdmin.
        </Alert>
      )}

      {cart.count === 0 ? (
        <Alert severity="info">
          Select entitlements first.{' '}
          <Button component={RouterLink} to="/entitlements" size="small">
            Search
          </Button>
        </Alert>
      ) : (
        <Stack component="form" spacing={2} onSubmit={onSubmit} sx={{ maxWidth: 560 }}>
          <Typography>
            {cart.count} entitlement{cart.count === 1 ? '' : 's'} will be included.
          </Typography>
          <TextField
            label="Extension days"
            type="number"
            value={extensionDays}
            onChange={(e) => setExtensionDays(Number(e.target.value))}
            required
            inputProps={{ min: 1, max: 3650, 'aria-label': 'Extension days' }}
            helperText="Applied uniformly from each entitlement’s current expiry"
          />
          <TextField
            label="Extension number (optional)"
            value={extensionNumber}
            onChange={(e) => setExtensionNumber(e.target.value)}
            helperText="Leave blank to auto-generate EXT-…"
          />
          <ErrorAlert error={mutation.error} title="Create failed" />
          <Button
            type="submit"
            variant="contained"
            disabled={!canCreate || mutation.isPending || cart.count === 0}
          >
            {mutation.isPending ? 'Submitting…' : 'Submit request'}
          </Button>
        </Stack>
      )}
    </>
  )
}

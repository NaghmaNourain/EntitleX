import Alert from '@mui/material/Alert'
import Button from '@mui/material/Button'
import FormControl from '@mui/material/FormControl'
import InputLabel from '@mui/material/InputLabel'
import MenuItem from '@mui/material/MenuItem'
import Select from '@mui/material/Select'
import Stack from '@mui/material/Stack'
import TextField from '@mui/material/TextField'
import Typography from '@mui/material/Typography'
import { useMutation } from '@tanstack/react-query'
import { useMemo, useState, type FormEvent } from 'react'
import { Link as RouterLink, useNavigate } from 'react-router-dom'
import { api } from '../api/endpoints'
import type { NormalizedEntitlement } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import { ErrorAlert } from '../components/ErrorAlert'
import { PageHeader } from '../components/PageHeader'
import { useSelectionCart } from '../hooks/useSelectionCart'

const PROVIDERS = ['CloudShield', 'NetworkGuard'] as const

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
  const [manualProvider, setManualProvider] = useState<(typeof PROVIDERS)[number]>('CloudShield')
  const [manualEntitlementId, setManualEntitlementId] = useState('')
  const [manualCustomerId, setManualCustomerId] = useState('')
  const [manualExpiryUtc, setManualExpiryUtc] = useState('')
  const [manualError, setManualError] = useState<string | null>(null)
  const canCreate = hasRole('RenewalRepresentative', 'DealDeskManager', 'PlatformAdmin')
  const canSearch = hasRole('RenewalRepresentative', 'DealDeskManager', 'PlatformAdmin')

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
    if (extensionDays < 1 || extensionDays > 365) return
    mutation.mutate()
  }

  function onAddKnownEntitlement(event: FormEvent) {
    event.preventDefault()
    setManualError(null)
    const entitlementId = manualEntitlementId.trim()
    const customerId = manualCustomerId.trim()
    if (!entitlementId) {
      setManualError('Entitlement ID is required.')
      return
    }
    if (!customerId) {
      setManualError('Customer ID is required for ACL checks on submit.')
      return
    }
    let expiryUtc = manualExpiryUtc.trim()
    if (!expiryUtc) {
      expiryUtc = new Date().toISOString()
    } else {
      const parsed = new Date(expiryUtc)
      if (Number.isNaN(parsed.getTime())) {
        setManualError('Current expiry must be a valid date/time (ISO-8601).')
        return
      }
      expiryUtc = parsed.toISOString()
    }

    const item: NormalizedEntitlement = {
      entitlementId,
      provider: manualProvider,
      customerId,
      productCode: '',
      productGroup: null,
      displayName: entitlementId,
      expiryUtc,
      updatedAtUtc: null,
    }

    if (cart.isSelected(item)) {
      setManualError('That entitlement is already in the selection.')
      return
    }

    cart.toggle(item)
    setManualEntitlementId('')
    setManualExpiryUtc('')
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
          Creating requests requires RenewalRepresentative, DealDeskManager, or PlatformAdmin.
        </Alert>
      )}

      {canCreate && (
        <Stack
          component="form"
          spacing={2}
          onSubmit={onAddKnownEntitlement}
          sx={{ maxWidth: 560, mb: 3 }}
        >
          <Typography variant="h6">Add by known entitlement ID</Typography>
          <Typography variant="body2" color="text.secondary">
            Renewal reps can create without cross-customer search when entitlement IDs are already
            known. The API re-fetches provider expiry and enforces customer ACL on submit.
          </Typography>
          <FormControl fullWidth>
            <InputLabel id="manual-provider-label">Provider</InputLabel>
            <Select
              labelId="manual-provider-label"
              label="Provider"
              value={manualProvider}
              onChange={(e) =>
                setManualProvider(e.target.value as (typeof PROVIDERS)[number])
              }
            >
              {PROVIDERS.map((p) => (
                <MenuItem key={p} value={p}>
                  {p}
                </MenuItem>
              ))}
            </Select>
          </FormControl>
          <TextField
            label="Entitlement ID"
            value={manualEntitlementId}
            onChange={(e) => setManualEntitlementId(e.target.value)}
            required
            inputProps={{ 'aria-label': 'Entitlement ID' }}
          />
          <TextField
            label="Customer ID"
            value={manualCustomerId}
            onChange={(e) => setManualCustomerId(e.target.value)}
            required
            helperText="Must match a customer you are allowed to access."
            inputProps={{ 'aria-label': 'Customer ID' }}
          />
          <TextField
            label="Current expiry (optional hint)"
            value={manualExpiryUtc}
            onChange={(e) => setManualExpiryUtc(e.target.value)}
            helperText="ISO-8601; server replaces this with provider truth on submit."
            inputProps={{ 'aria-label': 'Current expiry hint' }}
          />
          {manualError && <Alert severity="error">{manualError}</Alert>}
          <Button type="submit" variant="outlined">
            Add entitlement
          </Button>
          {canSearch && (
            <Button component={RouterLink} to="/entitlements" size="small">
              Or search entitlements
            </Button>
          )}
        </Stack>
      )}

      {cart.count === 0 ? (
        <Alert severity="info">
          Add at least one entitlement above
          {canSearch ? (
            <>
              {' '}
              or{' '}
              <Button component={RouterLink} to="/entitlements" size="small">
                search
              </Button>
            </>
          ) : null}
          .
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
            inputProps={{ min: 1, max: 365, 'aria-label': 'Extension days' }}
            helperText="1–365 days from each entitlement’s current expiry (domain max)"
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

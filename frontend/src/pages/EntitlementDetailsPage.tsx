import Button from '@mui/material/Button'
import Paper from '@mui/material/Paper'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { useQuery } from '@tanstack/react-query'
import { Link as RouterLink, useParams, useSearchParams } from 'react-router-dom'
import { api } from '../api/endpoints'
import { ErrorAlert } from '../components/ErrorAlert'
import { LoadingState } from '../components/LoadingState'
import { PageHeader } from '../components/PageHeader'
import { useSelectionCart } from '../hooks/useSelectionCart'

export function EntitlementDetailsPage() {
  const { provider = '', entitlementId = '' } = useParams()
  const [params] = useSearchParams()
  const customerId = params.get('customerId') ?? ''
  const cart = useSelectionCart()

  const query = useQuery({
    queryKey: ['entitlements', customerId],
    queryFn: ({ signal }) => api.searchEntitlements(customerId, signal),
    enabled: customerId.trim().length > 0,
  })

  const entitlement = query.data?.entitlements.find(
    (e) =>
      e.provider.toLowerCase() === decodeURIComponent(provider).toLowerCase() &&
      e.entitlementId === decodeURIComponent(entitlementId),
  )

  return (
    <>
      <PageHeader
        title="Unified entitlement details"
        subtitle="Normalized control-plane view across providers."
        actions={
          <Button component={RouterLink} to={`/entitlements?customerId=${encodeURIComponent(customerId)}`}>
            Back to search
          </Button>
        }
      />

      {!customerId && (
        <Typography color="text.secondary">
          Open this page from entitlement search so the customer context is available.
        </Typography>
      )}

      {query.isFetching && <LoadingState />}
      <ErrorAlert error={query.error} />

      {entitlement && (
        <Paper variant="outlined" sx={{ p: 3 }}>
          <Stack spacing={1.5}>
            <Typography variant="h5">{entitlement.displayName}</Typography>
            <Detail label="Entitlement ID" value={entitlement.entitlementId} mono />
            <Detail label="Provider" value={entitlement.provider} />
            <Detail label="Customer" value={entitlement.customerId} />
            <Detail label="Product code" value={entitlement.productCode} />
            <Detail label="Product group" value={entitlement.productGroup ?? '—'} />
            <Detail label="Expiry (UTC)" value={new Date(entitlement.expiryUtc).toISOString()} />
            <Detail
              label="Updated (UTC)"
              value={entitlement.updatedAtUtc ? new Date(entitlement.updatedAtUtc).toISOString() : '—'}
            />
            <Stack direction="row" spacing={1} sx={{ pt: 1 }}>
              <Button
                variant="contained"
                onClick={() => {
                  if (!cart.isSelected(entitlement)) cart.toggle(entitlement)
                }}
              >
                Add to bulk selection
              </Button>
              <Button component={RouterLink} to="/extensions/bulk" variant="outlined">
                Review selection
              </Button>
            </Stack>
          </Stack>
        </Paper>
      )}

      {query.data && !entitlement && customerId && !query.isFetching && (
        <Typography color="text.secondary">Entitlement not found in the latest aggregation.</Typography>
      )}
    </>
  )
}

function Detail({ label, value, mono }: { label: string; value: string; mono?: boolean }) {
  return (
    <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
      <Typography sx={{ minWidth: 160 }} color="text.secondary">
        {label}
      </Typography>
      <Typography sx={mono ? { fontFamily: 'IBM Plex Mono, monospace' } : undefined}>{value}</Typography>
    </Stack>
  )
}

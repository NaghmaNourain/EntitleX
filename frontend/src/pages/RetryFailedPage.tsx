import Alert from '@mui/material/Alert'
import Button from '@mui/material/Button'
import Checkbox from '@mui/material/Checkbox'
import Paper from '@mui/material/Paper'
import Stack from '@mui/material/Stack'
import Table from '@mui/material/Table'
import TableBody from '@mui/material/TableBody'
import TableCell from '@mui/material/TableCell'
import TableHead from '@mui/material/TableHead'
import TableRow from '@mui/material/TableRow'
import TextField from '@mui/material/TextField'
import Typography from '@mui/material/Typography'
import { useMutation, useQuery } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import { Link as RouterLink, useNavigate, useParams } from 'react-router-dom'
import { api } from '../api/endpoints'
import { createCorrelationId } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import { ErrorAlert } from '../components/ErrorAlert'
import { LoadingState } from '../components/LoadingState'
import { PageHeader } from '../components/PageHeader'
import { RequestIdField } from '../components/RequestIdField'
import { StatusChip } from '../components/StatusChip'

export function RetryFailedPage() {
  const { requestId = '' } = useParams()
  const navigate = useNavigate()
  const { hasRole } = useAuth()
  const canRetry = hasRole('RenewalRepresentative', 'PlatformAdmin')
  const [notes, setNotes] = useState('')
  const [selectedLogicalOps, setSelectedLogicalOps] = useState<string[]>([])
  const [idempotencyKey, setIdempotencyKey] = useState(() => createCorrelationId())

  const requestQuery = useQuery({
    queryKey: ['request', requestId],
    queryFn: ({ signal }) => api.getLicenseExtensionRequest(requestId, signal),
    enabled: Boolean(requestId),
  })

  const sagaQuery = useQuery({
    queryKey: ['saga', requestId],
    queryFn: ({ signal }) => api.getSaga(requestId, signal),
    enabled: Boolean(requestId),
  })

  const retryEligible = useMemo(
    () =>
      (sagaQuery.data?.itemOutcomes ?? []).filter(
        (i) => i.status === 'Failed',
      ),
    [sagaQuery.data],
  )

  const untouchedSuccesses = useMemo(
    () =>
      (sagaQuery.data?.itemOutcomes ?? []).filter((i) => i.status === 'Succeeded'),
    [sagaQuery.data],
  )

  const uncertain = useMemo(
    () =>
      (sagaQuery.data?.itemOutcomes ?? []).filter((i) => i.status === 'Uncertain'),
    [sagaQuery.data],
  )

  useEffect(() => {
    setSelectedLogicalOps(retryEligible.map((i) => i.logicalOperationId))
  }, [retryEligible])

  const mutation = useMutation({
    mutationFn: () =>
      api.retryFailed(
        requestId,
        {
          notes: notes.trim() || undefined,
          logicalOperationIds:
            selectedLogicalOps.length === retryEligible.length
              ? undefined
              : selectedLogicalOps,
        },
        idempotencyKey,
      ),
    onSuccess: (created) => {
      navigate(`/requests/${created.id}`)
    },
  })

  function toggle(logicalOperationId: string) {
    setSelectedLogicalOps((prev) =>
      prev.includes(logicalOperationId)
        ? prev.filter((id) => id !== logicalOperationId)
        : [...prev, logicalOperationId],
    )
  }

  return (
    <>
      <PageHeader
        title="Retry failed extensions"
        subtitle="Selective retry copies only failed items, keeps LogicalOperationId, and leaves successful extensions untouched (forward recovery)."
      />
      <RequestIdField initial={requestId} onSubmit={(id) => navigate(`/retry/${id}`)} />

      {!canRetry && (
        <Alert severity="warning" sx={{ mb: 2 }}>
          Retry requires RenewalRepresentative or PlatformAdmin.
        </Alert>
      )}

      {(requestQuery.isLoading || sagaQuery.isLoading) && <LoadingState />}
      <ErrorAlert error={requestQuery.error} />
      <ErrorAlert error={sagaQuery.error} />
      <ErrorAlert error={mutation.error} title="Retry failed" />

      {sagaQuery.data && (
        <Stack spacing={2}>
          <Alert severity="info">
            Source request <strong>{requestQuery.data?.extensionNumber ?? requestId}</strong> · status{' '}
            <StatusChip status={requestQuery.data?.status ?? sagaQuery.data.requestStatusProjection} />
          </Alert>

          <Paper variant="outlined" sx={{ p: 2, borderColor: 'error.light', bgcolor: 'rgba(180,35,24,0.04)' }}>
            <Typography variant="h6" gutterBottom>
              Will retry ({selectedLogicalOps.length}/{retryEligible.length} selected)
            </Typography>
            <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
              Failed items only. Uncertain items are listed separately and are not retried blindly.
            </Typography>
            <Table size="small" aria-label="Items that will retry">
              <TableHead>
                <TableRow>
                  <TableCell padding="checkbox">Retry</TableCell>
                  <TableCell>Entitlement</TableCell>
                  <TableCell>Logical op</TableCell>
                  <TableCell>Error</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {retryEligible.map((item) => (
                  <TableRow key={item.id} selected={selectedLogicalOps.includes(item.logicalOperationId)}>
                    <TableCell padding="checkbox">
                      <Checkbox
                        checked={selectedLogicalOps.includes(item.logicalOperationId)}
                        onChange={() => toggle(item.logicalOperationId)}
                        inputProps={{ 'aria-label': `Retry ${item.entitlementId}` }}
                      />
                    </TableCell>
                    <TableCell sx={{ fontFamily: 'IBM Plex Mono, monospace', fontSize: 12 }}>
                      {item.entitlementId}
                    </TableCell>
                    <TableCell sx={{ fontFamily: 'IBM Plex Mono, monospace', fontSize: 12 }}>
                      {item.logicalOperationId}
                    </TableCell>
                    <TableCell>
                      {item.errorCode ?? ''} {item.errorMessage ?? ''}
                    </TableCell>
                  </TableRow>
                ))}
                {retryEligible.length === 0 && (
                  <TableRow>
                    <TableCell colSpan={4}>
                      <Typography color="text.secondary">No failed items to retry.</Typography>
                    </TableCell>
                  </TableRow>
                )}
              </TableBody>
            </Table>
          </Paper>

          <Paper variant="outlined" sx={{ p: 2, borderColor: 'success.light', bgcolor: 'rgba(6,118,71,0.04)' }}>
            <Typography variant="h6" gutterBottom>
              Untouched successes ({untouchedSuccesses.length})
            </Typography>
            <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
              These remain as-is. Retry never revokes or re-applies a successful extension.
            </Typography>
            <Table size="small" aria-label="Untouched successful items">
              <TableHead>
                <TableRow>
                  <TableCell>Entitlement</TableCell>
                  <TableCell>Logical op</TableCell>
                  <TableCell>Status</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {untouchedSuccesses.map((item) => (
                  <TableRow key={item.id}>
                    <TableCell sx={{ fontFamily: 'IBM Plex Mono, monospace', fontSize: 12 }}>
                      {item.entitlementId}
                    </TableCell>
                    <TableCell sx={{ fontFamily: 'IBM Plex Mono, monospace', fontSize: 12 }}>
                      {item.logicalOperationId}
                    </TableCell>
                    <TableCell>
                      <StatusChip status={item.status} />
                    </TableCell>
                  </TableRow>
                ))}
                {untouchedSuccesses.length === 0 && (
                  <TableRow>
                    <TableCell colSpan={3}>
                      <Typography color="text.secondary">No successful items on this request.</Typography>
                    </TableCell>
                  </TableRow>
                )}
              </TableBody>
            </Table>
          </Paper>

          {uncertain.length > 0 && (
            <Alert severity="warning">
              {uncertain.length} uncertain item(s) — use reconciliation before retrying. See{' '}
              <Button component={RouterLink} to="/ops" size="small">
                Ops monitoring
              </Button>
            </Alert>
          )}

          <Stack spacing={2} sx={{ maxWidth: 560 }}>
            <TextField
              label="Notes"
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              multiline
              minRows={2}
            />
            <TextField
              label="Idempotency-Key"
              value={idempotencyKey}
              onChange={(e) => setIdempotencyKey(e.target.value)}
              helperText="Required. Replays with the same key return the prior retry request."
              required
            />
            <Button
              variant="contained"
              disabled={
                !canRetry ||
                selectedLogicalOps.length === 0 ||
                mutation.isPending ||
                !idempotencyKey.trim()
              }
              onClick={() => mutation.mutate()}
            >
              {mutation.isPending ? 'Submitting retry…' : 'Create retry request'}
            </Button>
          </Stack>
        </Stack>
      )}
    </>
  )
}

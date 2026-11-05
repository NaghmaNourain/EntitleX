import Alert from '@mui/material/Alert'
import Button from '@mui/material/Button'
import Grid from '@mui/material/Grid'
import Paper from '@mui/material/Paper'
import Stack from '@mui/material/Stack'
import Table from '@mui/material/Table'
import TableBody from '@mui/material/TableBody'
import TableCell from '@mui/material/TableCell'
import TableHead from '@mui/material/TableHead'
import TableRow from '@mui/material/TableRow'
import Typography from '@mui/material/Typography'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { Link as RouterLink } from 'react-router-dom'
import { api } from '../api/endpoints'
import { ErrorAlert } from '../components/ErrorAlert'
import { LoadingState } from '../components/LoadingState'
import { PageHeader } from '../components/PageHeader'
import { StatusChip } from '../components/StatusChip'

export function OpsMonitoringPage() {
  const qc = useQueryClient()

  const health = useQuery({
    queryKey: ['health'],
    queryFn: ({ signal }) => api.getHealth(signal),
    refetchInterval: 15000,
  })

  const ready = useQuery({
    queryKey: ['health-ready'],
    queryFn: ({ signal }) => api.getReady(signal),
    refetchInterval: 15000,
  })

  const providers = useQuery({
    queryKey: ['health-providers'],
    queryFn: ({ signal }) => api.getProviderHealth(signal),
    refetchInterval: 15000,
  })

  const summary = useQuery({
    queryKey: ['ops-summary'],
    queryFn: ({ signal }) => api.getOpsSummary(25, signal),
    refetchInterval: 15000,
  })

  const reconcile = useMutation({
    mutationFn: (requestId: string) => api.manualReconcile(requestId),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['ops-summary'] })
    },
  })

  return (
    <>
      <PageHeader
        title="Operational monitoring"
        subtitle="API/provider health, pending approvals, and reconciliation-required sagas."
      />

      <Grid container spacing={2} sx={{ mb: 3 }}>
        <Grid size={{ xs: 12, md: 4 }}>
          <MetricCard
            title="API /health"
            loading={health.isLoading}
            error={health.error}
            body={
              health.data ? (
                <StatusChip status={health.data.status} />
              ) : (
                <Typography color="text.secondary">—</Typography>
              )
            }
          />
        </Grid>
        <Grid size={{ xs: 12, md: 4 }}>
          <MetricCard
            title="Ready"
            loading={ready.isLoading}
            error={ready.error}
            body={
              <Typography variant="body2" sx={{ fontFamily: 'IBM Plex Mono, monospace', fontSize: 12 }}>
                {ready.data ? JSON.stringify(ready.data).slice(0, 120) : '—'}
              </Typography>
            }
          />
        </Grid>
        <Grid size={{ xs: 12, md: 4 }}>
          <MetricCard
            title="Providers"
            loading={providers.isLoading}
            error={providers.error}
            body={
              <Typography variant="body2" sx={{ fontFamily: 'IBM Plex Mono, monospace', fontSize: 12 }}>
                {providers.data ? JSON.stringify(providers.data).slice(0, 120) : '—'}
              </Typography>
            }
          />
        </Grid>
      </Grid>

      {summary.isLoading && <LoadingState />}
      <ErrorAlert error={summary.error} />
      <ErrorAlert error={reconcile.error} title="Manual reconcile failed" />

      {summary.data && (
        <Stack spacing={2}>
          <Alert severity="info">
            Generated {new Date(summary.data.generatedAtUtc).toISOString()} · pending approvals{' '}
            <strong>{summary.data.pendingApprovalCount}</strong> · recon required{' '}
            <strong>{summary.data.reconciliationRequiredCount}</strong>
          </Alert>

          <Paper variant="outlined" sx={{ p: 2 }}>
            <Typography variant="h6" gutterBottom>
              Pending approvals (sample)
            </Typography>
            <Table size="small" aria-label="Pending approvals sample">
              <TableHead>
                <TableRow>
                  <TableCell>Request</TableCell>
                  <TableCell>Band</TableCell>
                  <TableCell>Stage role</TableCell>
                  <TableCell>Created</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {summary.data.pendingApprovals.map((row) => (
                  <TableRow key={row.requestId}>
                    <TableCell>
                      <Button component={RouterLink} to={`/requests/${row.requestId}`} size="small">
                        {row.requestId.slice(0, 8)}…
                      </Button>
                    </TableCell>
                    <TableCell>{row.band}</TableCell>
                    <TableCell>{row.currentStageRole ?? '—'}</TableCell>
                    <TableCell>{new Date(row.createdAtUtc).toISOString()}</TableCell>
                  </TableRow>
                ))}
                {summary.data.pendingApprovals.length === 0 && (
                  <TableRow>
                    <TableCell colSpan={4}>
                      <Typography color="text.secondary">None</Typography>
                    </TableCell>
                  </TableRow>
                )}
              </TableBody>
            </Table>
          </Paper>

          <Paper variant="outlined" sx={{ p: 2 }}>
            <Typography variant="h6" gutterBottom>
              Reconciliation required
            </Typography>
            <Table size="small" aria-label="Reconciliation required">
              <TableHead>
                <TableRow>
                  <TableCell>Request</TableCell>
                  <TableCell>Saga state</TableCell>
                  <TableCell>S / F / P</TableCell>
                  <TableCell>Updated</TableCell>
                  <TableCell>Action</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {summary.data.reconciliationRequired.map((row) => (
                  <TableRow key={row.requestId}>
                    <TableCell>
                      <Button component={RouterLink} to={`/fulfillment/${row.requestId}`} size="small">
                        {row.requestId.slice(0, 8)}…
                      </Button>
                    </TableCell>
                    <TableCell>
                      <StatusChip status={row.sagaState} />
                    </TableCell>
                    <TableCell>
                      {row.succeededItemCount}/{row.failedItemCount}/{row.pendingItemCount}
                    </TableCell>
                    <TableCell>{new Date(row.updatedAtUtc).toISOString()}</TableCell>
                    <TableCell>
                      <Button
                        size="small"
                        variant="outlined"
                        disabled={reconcile.isPending}
                        onClick={() => reconcile.mutate(row.requestId)}
                      >
                        Reconcile
                      </Button>
                    </TableCell>
                  </TableRow>
                ))}
                {summary.data.reconciliationRequired.length === 0 && (
                  <TableRow>
                    <TableCell colSpan={5}>
                      <Typography color="text.secondary">None</Typography>
                    </TableCell>
                  </TableRow>
                )}
              </TableBody>
            </Table>
          </Paper>
        </Stack>
      )}
    </>
  )
}

function MetricCard({
  title,
  body,
  loading,
  error,
}: {
  title: string
  body: ReactNode
  loading: boolean
  error: unknown
}) {
  return (
    <Paper variant="outlined" sx={{ p: 2, height: '100%' }}>
      <Typography variant="subtitle2" gutterBottom>
        {title}
      </Typography>
      {loading ? <LoadingState label="…" /> : body}
      <ErrorAlert error={error} title={`${title} failed`} />
    </Paper>
  )
}

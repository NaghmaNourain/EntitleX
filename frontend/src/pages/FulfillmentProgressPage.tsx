import LinearProgress from '@mui/material/LinearProgress'
import Paper from '@mui/material/Paper'
import Stack from '@mui/material/Stack'
import Table from '@mui/material/Table'
import TableBody from '@mui/material/TableBody'
import TableCell from '@mui/material/TableCell'
import TableHead from '@mui/material/TableHead'
import TableRow from '@mui/material/TableRow'
import Typography from '@mui/material/Typography'
import { useQuery } from '@tanstack/react-query'
import { useNavigate, useParams } from 'react-router-dom'
import { api } from '../api/endpoints'
import { ErrorAlert } from '../components/ErrorAlert'
import { LoadingState } from '../components/LoadingState'
import { PageHeader } from '../components/PageHeader'
import { RequestIdField } from '../components/RequestIdField'
import { StatusChip } from '../components/StatusChip'

export function FulfillmentProgressPage() {
  const { requestId = '' } = useParams()
  const navigate = useNavigate()

  const query = useQuery({
    queryKey: ['saga', requestId],
    queryFn: ({ signal }) => api.getSaga(requestId, signal),
    enabled: Boolean(requestId),
    refetchInterval: 5000,
  })

  const total =
    (query.data?.succeededItemCount ?? 0) +
    (query.data?.failedItemCount ?? 0) +
    (query.data?.pendingItemCount ?? 0)
  const succeededPct = total === 0 ? 0 : ((query.data?.succeededItemCount ?? 0) / total) * 100

  return (
    <>
      <PageHeader
        title="Fulfillment progress"
        subtitle="Live saga state, item outcomes, and transition history (polls every 5s)."
      />
      <RequestIdField
        initial={requestId}
        onSubmit={(id) => navigate(`/fulfillment/${id}`)}
        buttonLabel="Track"
      />

      {requestId && query.isLoading && <LoadingState />}
      <ErrorAlert error={query.error} />

      {query.data && (
        <Stack spacing={2}>
          <Paper variant="outlined" sx={{ p: 2 }}>
            <Stack direction="row" spacing={2} flexWrap="wrap" useFlexGap alignItems="center" sx={{ mb: 2 }}>
              <StatusChip status={query.data.state} />
              <Typography variant="body2">Band {query.data.approvalBand}</Typography>
              <Typography variant="body2">
                Timeout {query.data.timeoutKind}
                {query.data.timeoutDeadlineUtc
                  ? ` · deadline ${new Date(query.data.timeoutDeadlineUtc).toISOString()}`
                  : ''}
              </Typography>
            </Stack>
            <Typography variant="body2" gutterBottom>
              Succeeded {query.data.succeededItemCount} · Failed {query.data.failedItemCount} · Pending{' '}
              {query.data.pendingItemCount}
            </Typography>
            <LinearProgress
              variant="determinate"
              value={succeededPct}
              aria-label="Succeeded item progress"
              sx={{ height: 10, borderRadius: 1 }}
            />
          </Paper>

          <Paper variant="outlined">
            <Table aria-label="Item outcomes">
              <TableHead>
                <TableRow>
                  <TableCell>Entitlement</TableCell>
                  <TableCell>Logical op</TableCell>
                  <TableCell>Status</TableCell>
                  <TableCell>Error</TableCell>
                  <TableCell>Updated</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {query.data.itemOutcomes.map((item) => (
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
                    <TableCell>
                      {item.errorCode || item.errorMessage
                        ? `${item.errorCode ?? ''} ${item.errorMessage ?? ''}`.trim()
                        : '—'}
                    </TableCell>
                    <TableCell>{new Date(item.updatedAtUtc).toISOString()}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </Paper>

          <Paper variant="outlined" sx={{ p: 2 }}>
            <Typography variant="h6" gutterBottom>
              Transitions
            </Typography>
            {query.data.transitions.map((t) => (
              <Typography key={t.id} variant="body2" sx={{ mb: 0.5 }}>
                {new Date(t.occurredAtUtc).toISOString()} · {t.fromState} → {t.toState} · {t.reason}
                {t.actor ? ` · ${t.actor}` : ''}
              </Typography>
            ))}
          </Paper>
        </Stack>
      )}
    </>
  )
}

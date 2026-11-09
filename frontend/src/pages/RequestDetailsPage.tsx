import Button from '@mui/material/Button'
import Paper from '@mui/material/Paper'
import Stack from '@mui/material/Stack'
import Table from '@mui/material/Table'
import TableBody from '@mui/material/TableBody'
import TableCell from '@mui/material/TableCell'
import TableHead from '@mui/material/TableHead'
import TableRow from '@mui/material/TableRow'
import Typography from '@mui/material/Typography'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link as RouterLink, useNavigate, useParams } from 'react-router-dom'
import { api } from '../api/endpoints'
import { useAuth } from '../auth/AuthContext'
import { ErrorAlert } from '../components/ErrorAlert'
import { LoadingState } from '../components/LoadingState'
import { PageHeader } from '../components/PageHeader'
import { RequestIdField } from '../components/RequestIdField'
import { RequestInsightPanel } from '../components/RequestInsightPanel'
import { StatusChip } from '../components/StatusChip'

export function RequestDetailsPage() {
  const { requestId = '' } = useParams()
  const navigate = useNavigate()
  const { hasRole } = useAuth()
  const qc = useQueryClient()

  const requestQuery = useQuery({
    queryKey: ['request', requestId],
    queryFn: ({ signal }) => api.getLicenseExtensionRequest(requestId, signal),
    enabled: Boolean(requestId),
  })

  const approvalQuery = useQuery({
    queryKey: ['approval', requestId],
    queryFn: ({ signal }) => api.getApproval(requestId, signal),
    enabled: Boolean(requestId),
    retry: false,
  })

  const startApproval = useMutation({
    mutationFn: () => api.startApproval(requestId),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['approval', requestId] })
      void qc.invalidateQueries({ queryKey: ['request', requestId] })
    },
  })

  if (!requestId) {
    return (
      <>
        <PageHeader title="Extension request details" />
        <RequestIdField onSubmit={(id) => navigate(`/requests/${id}`)} />
      </>
    )
  }

  return (
    <>
      <PageHeader
        title="Extension request details"
        subtitle={requestQuery.data?.extensionNumber ?? requestId}
        actions={
          <>
            <Button component={RouterLink} to={`/fulfillment/${requestId}`} variant="outlined">
              Fulfillment
            </Button>
            <Button component={RouterLink} to={`/lineage/${requestId}`} variant="outlined">
              Lineage
            </Button>
            <Button component={RouterLink} to={`/audit/${requestId}`} variant="outlined">
              Audit
            </Button>
            <Button component={RouterLink} to={`/failures/${requestId}`} variant="outlined">
              Failures
            </Button>
            <Button component={RouterLink} to={`/retry/${requestId}`} variant="contained">
              Retry failed
            </Button>
          </>
        }
      />

      {requestQuery.isLoading && <LoadingState />}
      <ErrorAlert error={requestQuery.error} />
      <ErrorAlert error={startApproval.error} title="Start approval failed" />

      {requestQuery.data && (
        <Stack spacing={2}>
          <RequestInsightPanel
            requestId={requestId}
            mode="explain"
            title="Explain this request"
            buttonLabel="Explain"
          />
          <Paper variant="outlined" sx={{ p: 2 }}>
            <Stack direction="row" spacing={2} flexWrap="wrap" useFlexGap alignItems="center">
              <StatusChip status={requestQuery.data.status} />
              <Typography variant="body2">Requested by {requestQuery.data.requestedBy}</Typography>
              <Typography variant="body2">
                Created {new Date(requestQuery.data.createdAtUtc).toISOString()}
              </Typography>
              {requestQuery.data.parentId && (
                <Button
                  size="small"
                  component={RouterLink}
                  to={`/requests/${requestQuery.data.parentId}`}
                >
                  Parent {requestQuery.data.parentId.slice(0, 8)}…
                </Button>
              )}
              {hasRole('RenewalRepresentative', 'PlatformAdmin') && (
                <Button
                  size="small"
                  variant="outlined"
                  onClick={() => startApproval.mutate()}
                  disabled={startApproval.isPending}
                >
                  Start / ensure approval
                </Button>
              )}
            </Stack>
          </Paper>

          <Paper variant="outlined">
            <Table aria-label="Request items">
              <TableHead>
                <TableRow>
                  <TableCell>Entitlement</TableCell>
                  <TableCell>Provider</TableCell>
                  <TableCell>Logical op</TableCell>
                  <TableCell>Current expiry</TableCell>
                  <TableCell>Requested expiry</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {requestQuery.data.items.map((item) => (
                  <TableRow key={item.id}>
                    <TableCell sx={{ fontFamily: 'IBM Plex Mono, monospace' }}>
                      {item.entitlementId}
                    </TableCell>
                    <TableCell>{item.provider}</TableCell>
                    <TableCell sx={{ fontFamily: 'IBM Plex Mono, monospace', fontSize: 12 }}>
                      {item.logicalOperationId}
                    </TableCell>
                    <TableCell>{new Date(item.currentExpiryUtc).toISOString()}</TableCell>
                    <TableCell>{new Date(item.requestedExpiryUtc).toISOString()}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </Paper>

          {approvalQuery.data && (
            <Paper variant="outlined" sx={{ p: 2 }}>
              <Typography variant="h6" gutterBottom>
                Approval
              </Typography>
              <Stack direction="row" spacing={1} alignItems="center" sx={{ mb: 1 }}>
                <StatusChip status={approvalQuery.data.status} />
                <Typography variant="body2">
                  Band {approvalQuery.data.band} · max {approvalQuery.data.maxExtensionDays}d
                </Typography>
              </Stack>
              <Typography variant="subtitle2">Stages</Typography>
              {approvalQuery.data.stages.map((s) => (
                <Typography key={s.id} variant="body2">
                  #{s.order} {s.requiredRole} — {s.status}
                </Typography>
              ))}
            </Paper>
          )}
        </Stack>
      )}
    </>
  )
}

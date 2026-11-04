import Alert from '@mui/material/Alert'
import Button from '@mui/material/Button'
import Paper from '@mui/material/Paper'
import Stack from '@mui/material/Stack'
import Table from '@mui/material/Table'
import TableBody from '@mui/material/TableBody'
import TableCell from '@mui/material/TableCell'
import TableHead from '@mui/material/TableHead'
import TableRow from '@mui/material/TableRow'
import Typography from '@mui/material/Typography'
import { useQuery } from '@tanstack/react-query'
import { Link as RouterLink, useNavigate, useParams } from 'react-router-dom'
import { api } from '../api/endpoints'
import { ErrorAlert } from '../components/ErrorAlert'
import { LoadingState } from '../components/LoadingState'
import { PageHeader } from '../components/PageHeader'
import { RequestIdField } from '../components/RequestIdField'
import { StatusChip } from '../components/StatusChip'

export function FailedEntitlementDetailsPage() {
  const { requestId = '' } = useParams()
  const navigate = useNavigate()

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

  const failed = (sagaQuery.data?.itemOutcomes ?? []).filter(
    (i) => i.status === 'Failed' || i.status === 'Uncertain',
  )

  return (
    <>
      <PageHeader
        title="Failed entitlement details"
        subtitle="Failed and uncertain saga item outcomes for a request — candidates for selective retry."
        actions={
          requestId ? (
            <Button component={RouterLink} to={`/retry/${requestId}`} variant="contained">
              Open retry
            </Button>
          ) : undefined
        }
      />
      <RequestIdField initial={requestId} onSubmit={(id) => navigate(`/failures/${id}`)} />

      {(requestQuery.isLoading || sagaQuery.isLoading) && <LoadingState />}
      <ErrorAlert error={requestQuery.error} />
      <ErrorAlert error={sagaQuery.error} />

      {sagaQuery.data && failed.length === 0 && (
        <Alert severity="success">No failed or uncertain items on this request.</Alert>
      )}

      {failed.length > 0 && (
        <Paper variant="outlined">
          <Table aria-label="Failed entitlements">
            <TableHead>
              <TableRow>
                <TableCell>Entitlement</TableCell>
                <TableCell>Logical operation</TableCell>
                <TableCell>Status</TableCell>
                <TableCell>Error code</TableCell>
                <TableCell>Error message</TableCell>
                <TableCell>Request item</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {failed.map((item) => {
                const reqItem = requestQuery.data?.items.find((i) => i.id === item.requestItemId)
                return (
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
                    <TableCell>{item.errorCode ?? '—'}</TableCell>
                    <TableCell>{item.errorMessage ?? '—'}</TableCell>
                    <TableCell>
                      <Stack spacing={0.5}>
                        <Typography variant="caption">
                          Provider {reqItem?.provider ?? '—'}
                        </Typography>
                        <Typography variant="caption">
                          Attempt {reqItem?.attemptId?.slice(0, 8) ?? '—'}…
                        </Typography>
                      </Stack>
                    </TableCell>
                  </TableRow>
                )
              })}
            </TableBody>
          </Table>
        </Paper>
      )}
    </>
  )
}

import Alert from '@mui/material/Alert'
import Button from '@mui/material/Button'
import Paper from '@mui/material/Paper'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { useQuery } from '@tanstack/react-query'
import { Link as RouterLink, useNavigate, useParams } from 'react-router-dom'
import { api } from '../api/endpoints'
import { ErrorAlert } from '../components/ErrorAlert'
import { LoadingState } from '../components/LoadingState'
import { PageHeader } from '../components/PageHeader'
import { RequestIdField } from '../components/RequestIdField'
import { StatusChip } from '../components/StatusChip'

export function RequestLineagePage() {
  const { requestId = '' } = useParams()
  const navigate = useNavigate()

  const query = useQuery({
    queryKey: ['request', requestId],
    queryFn: ({ signal }) => api.getLicenseExtensionRequest(requestId, signal),
    enabled: Boolean(requestId),
  })

  const parentQuery = useQuery({
    queryKey: ['request', query.data?.parentId],
    queryFn: ({ signal }) => api.getLicenseExtensionRequest(query.data!.parentId!, signal),
    enabled: Boolean(query.data?.parentId),
  })

  return (
    <>
      <PageHeader
        title="Request lineage / retry history"
        subtitle="ParentId chain and RetryHistory entries for selective retries."
      />
      <RequestIdField initial={requestId} onSubmit={(id) => navigate(`/lineage/${id}`)} />

      {query.isLoading && <LoadingState />}
      <ErrorAlert error={query.error} />

      {query.data && (
        <Stack spacing={2}>
          <Paper variant="outlined" sx={{ p: 2 }}>
            <Typography variant="h6" gutterBottom>
              Current request
            </Typography>
            <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
              <Typography fontWeight={600}>{query.data.extensionNumber}</Typography>
              <StatusChip status={query.data.status} />
              <Button size="small" component={RouterLink} to={`/requests/${query.data.id}`}>
                Open details
              </Button>
            </Stack>
            <Typography variant="body2" sx={{ mt: 1, fontFamily: 'IBM Plex Mono, monospace' }}>
              {query.data.id}
            </Typography>
          </Paper>

          {query.data.parentId ? (
            <Paper variant="outlined" sx={{ p: 2 }}>
              <Typography variant="h6" gutterBottom>
                Parent
              </Typography>
              {parentQuery.isLoading && <LoadingState label="Loading parent…" />}
              {parentQuery.data ? (
                <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
                  <Typography>{parentQuery.data.extensionNumber}</Typography>
                  <StatusChip status={parentQuery.data.status} />
                  <Button
                    size="small"
                    component={RouterLink}
                    to={`/lineage/${parentQuery.data.id}`}
                  >
                    Walk parent lineage
                  </Button>
                </Stack>
              ) : (
                <Button component={RouterLink} to={`/requests/${query.data.parentId}`}>
                  Parent {query.data.parentId}
                </Button>
              )}
            </Paper>
          ) : (
            <Alert severity="info">This is a root request (no ParentId).</Alert>
          )}

          <Paper variant="outlined" sx={{ p: 2 }}>
            <Typography variant="h6" gutterBottom>
              Retry history ({query.data.retryHistory.length})
            </Typography>
            {query.data.retryHistory.length === 0 && (
              <Typography color="text.secondary">No child retries recorded on this request.</Typography>
            )}
            {query.data.retryHistory.map((entry) => (
              <Stack
                key={entry.id}
                direction={{ xs: 'column', sm: 'row' }}
                spacing={1}
                alignItems={{ sm: 'center' }}
                sx={{ py: 1, borderBottom: '1px solid', borderColor: 'divider' }}
              >
                <Typography fontWeight={600}>{entry.extensionNumber}</Typography>
                <Typography variant="body2">{entry.itemCount} items</Typography>
                <Typography variant="body2">by {entry.createdBy}</Typography>
                <Typography variant="caption" color="text.secondary">
                  {new Date(entry.createdAtUtc).toISOString()}
                </Typography>
                <Button size="small" component={RouterLink} to={`/requests/${entry.retryRequestId}`}>
                  Open retry
                </Button>
              </Stack>
            ))}
          </Paper>
        </Stack>
      )}
    </>
  )
}

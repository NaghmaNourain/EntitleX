import Button from '@mui/material/Button'
import Dialog from '@mui/material/Dialog'
import DialogActions from '@mui/material/DialogActions'
import DialogContent from '@mui/material/DialogContent'
import DialogTitle from '@mui/material/DialogTitle'
import Paper from '@mui/material/Paper'
import Stack from '@mui/material/Stack'
import Table from '@mui/material/Table'
import TableBody from '@mui/material/TableBody'
import TableCell from '@mui/material/TableCell'
import TableHead from '@mui/material/TableHead'
import TablePagination from '@mui/material/TablePagination'
import TableRow from '@mui/material/TableRow'
import TextField from '@mui/material/TextField'
import Typography from '@mui/material/Typography'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link as RouterLink } from 'react-router-dom'
import { api } from '../api/endpoints'
import { ErrorAlert } from '../components/ErrorAlert'
import { LoadingState } from '../components/LoadingState'
import { PageHeader } from '../components/PageHeader'
import { StatusChip } from '../components/StatusChip'

export function ApprovalQueuePage({
  role,
  title,
}: {
  role: 'RenewalManager' | 'DealDeskManager'
  title: string
}) {
  const [page, setPage] = useState(0)
  const [rowsPerPage, setRowsPerPage] = useState(10)
  const [activeRequestId, setActiveRequestId] = useState<string | null>(null)
  const [comment, setComment] = useState('')
  const qc = useQueryClient()

  const query = useQuery({
    queryKey: ['pending-approvals', role, page, rowsPerPage],
    queryFn: ({ signal }) =>
      api.listPendingApprovals(
        { role, take: rowsPerPage, skip: page * rowsPerPage },
        signal,
      ),
  })

  const decide = useMutation({
    mutationFn: (outcome: 'Approved' | 'Rejected') =>
      api.decideApproval(activeRequestId!, outcome, comment.trim() || undefined),
    onSuccess: () => {
      setActiveRequestId(null)
      setComment('')
      void qc.invalidateQueries({ queryKey: ['pending-approvals', role] })
    },
  })

  return (
    <>
      <PageHeader
        title={title}
        subtitle={`Pending approval cases where the current stage requires ${role}.`}
      />

      {query.isLoading && <LoadingState />}
      <ErrorAlert error={query.error} />
      <ErrorAlert error={decide.error} title="Decision failed" />

      {query.data && (
        <Paper variant="outlined">
          <Table aria-label={`${role} approval queue`}>
            <TableHead>
              <TableRow>
                <TableCell>Request</TableCell>
                <TableCell>Band</TableCell>
                <TableCell>Max days</TableCell>
                <TableCell>Stage role</TableCell>
                <TableCell>Created</TableCell>
                <TableCell>Actions</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {query.data.items.map((row) => (
                <TableRow key={row.approvalCaseId} hover>
                  <TableCell sx={{ fontFamily: 'IBM Plex Mono, monospace', fontSize: 12 }}>
                    <Button component={RouterLink} to={`/requests/${row.requestId}`} size="small">
                      {row.requestId.slice(0, 8)}…
                    </Button>
                  </TableCell>
                  <TableCell>
                    <StatusChip status={row.band} />
                  </TableCell>
                  <TableCell>{row.maxExtensionDays}</TableCell>
                  <TableCell>{row.currentStageRole ?? '—'}</TableCell>
                  <TableCell>{new Date(row.createdAtUtc).toISOString()}</TableCell>
                  <TableCell>
                    <Button
                      size="small"
                      variant="contained"
                      aria-label={`Decide approval for request ${row.requestId}`}
                      onClick={() => setActiveRequestId(row.requestId)}
                    >
                      Decide
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
              {query.data.items.length === 0 && (
                <TableRow>
                  <TableCell colSpan={6}>
                    <Typography color="text.secondary">Queue is empty.</Typography>
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
          <TablePagination
            component="div"
            count={query.data.total}
            page={page}
            onPageChange={(_, next) => setPage(next)}
            rowsPerPage={rowsPerPage}
            onRowsPerPageChange={(e) => {
              setRowsPerPage(parseInt(e.target.value, 10))
              setPage(0)
            }}
          />
        </Paper>
      )}

      <Dialog
        open={Boolean(activeRequestId)}
        onClose={() => setActiveRequestId(null)}
        fullWidth
        maxWidth="sm"
        aria-labelledby="decide-dialog-title"
      >
        <DialogTitle id="decide-dialog-title">Record approval decision</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ pt: 1 }}>
            <Typography variant="body2" sx={{ fontFamily: 'IBM Plex Mono, monospace' }}>
              {activeRequestId}
            </Typography>
            <TextField
              label="Comment (optional)"
              value={comment}
              onChange={(e) => setComment(e.target.value)}
              multiline
              minRows={2}
              fullWidth
            />
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setActiveRequestId(null)}>Cancel</Button>
          <Button
            color="error"
            disabled={decide.isPending}
            onClick={() => decide.mutate('Rejected')}
          >
            Reject
          </Button>
          <Button
            variant="contained"
            disabled={decide.isPending}
            onClick={() => decide.mutate('Approved')}
          >
            Approve
          </Button>
        </DialogActions>
      </Dialog>
    </>
  )
}

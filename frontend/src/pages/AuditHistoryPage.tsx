import Paper from '@mui/material/Paper'
import Table from '@mui/material/Table'
import TableBody from '@mui/material/TableBody'
import TableCell from '@mui/material/TableCell'
import TableHead from '@mui/material/TableHead'
import TableRow from '@mui/material/TableRow'
import TextField from '@mui/material/TextField'
import Typography from '@mui/material/Typography'
import { useQuery } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { api } from '../api/endpoints'
import { ErrorAlert } from '../components/ErrorAlert'
import { LoadingState } from '../components/LoadingState'
import { PageHeader } from '../components/PageHeader'
import { RequestIdField } from '../components/RequestIdField'

export function AuditHistoryPage() {
  const { requestId = '' } = useParams()
  const navigate = useNavigate()
  const [typeFilter, setTypeFilter] = useState('')

  const query = useQuery({
    queryKey: ['audit', requestId],
    queryFn: ({ signal }) => api.getAudit(requestId, signal),
    enabled: Boolean(requestId),
  })

  const events = useMemo(() => {
    const list = query.data?.events ?? []
    if (!typeFilter.trim()) return list
    return list.filter((e) =>
      e.eventType.toLowerCase().includes(typeFilter.trim().toLowerCase()),
    )
  }, [query.data, typeFilter])

  return (
    <>
      <PageHeader
        title="Audit history"
        subtitle="Reconciliation, reminder, and operational audit events for a request."
      />
      <RequestIdField initial={requestId} onSubmit={(id) => navigate(`/audit/${id}`)} />

      {query.isLoading && <LoadingState />}
      <ErrorAlert error={query.error} />

      {query.data && (
        <>
          <TextField
            label="Filter event type"
            value={typeFilter}
            onChange={(e) => setTypeFilter(e.target.value)}
            size="small"
            sx={{ mb: 2, maxWidth: 320 }}
          />
          <Paper variant="outlined">
            <Table aria-label="Audit events">
              <TableHead>
                <TableRow>
                  <TableCell>Occurred (UTC)</TableCell>
                  <TableCell>Type</TableCell>
                  <TableCell>Actor</TableCell>
                  <TableCell>Logical op</TableCell>
                  <TableCell>Detail</TableCell>
                  <TableCell>Correlation</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {events.map((e) => (
                  <TableRow key={e.id}>
                    <TableCell>{new Date(e.occurredAtUtc).toISOString()}</TableCell>
                    <TableCell sx={{ fontFamily: 'IBM Plex Mono, monospace', fontSize: 12 }}>
                      {e.eventType}
                    </TableCell>
                    <TableCell>{e.actor}</TableCell>
                    <TableCell sx={{ fontFamily: 'IBM Plex Mono, monospace', fontSize: 12 }}>
                      {e.logicalOperationId ?? '—'}
                    </TableCell>
                    <TableCell sx={{ maxWidth: 360 }}>{e.detail}</TableCell>
                    <TableCell sx={{ fontFamily: 'IBM Plex Mono, monospace', fontSize: 12 }}>
                      {e.correlationId ?? '—'}
                    </TableCell>
                  </TableRow>
                ))}
                {events.length === 0 && (
                  <TableRow>
                    <TableCell colSpan={6}>
                      <Typography color="text.secondary">No audit events for this request.</Typography>
                    </TableCell>
                  </TableRow>
                )}
              </TableBody>
            </Table>
          </Paper>
        </>
      )}
    </>
  )
}

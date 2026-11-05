import Alert from '@mui/material/Alert'
import Button from '@mui/material/Button'
import Checkbox from '@mui/material/Checkbox'
import FormControlLabel from '@mui/material/FormControlLabel'
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
import { useQuery } from '@tanstack/react-query'
import { useMemo, useState, type FormEvent } from 'react'
import { Link as RouterLink, useNavigate, useSearchParams } from 'react-router-dom'
import { api } from '../api/endpoints'
import { ErrorAlert } from '../components/ErrorAlert'
import { LoadingState } from '../components/LoadingState'
import { PageHeader } from '../components/PageHeader'
import { StatusChip } from '../components/StatusChip'
import { useSelectionCart } from '../hooks/useSelectionCart'

export function EntitlementSearchPage() {
  const [params, setParams] = useSearchParams()
  const navigate = useNavigate()
  const customerId = params.get('customerId') ?? ''
  const [draft, setDraft] = useState(customerId)
  const [providerFilter, setProviderFilter] = useState('')
  const [page, setPage] = useState(0)
  const [rowsPerPage, setRowsPerPage] = useState(10)
  const cart = useSelectionCart()

  const query = useQuery({
    queryKey: ['entitlements', customerId],
    queryFn: ({ signal }) => api.searchEntitlements(customerId, signal),
    enabled: customerId.trim().length > 0,
  })

  const filtered = useMemo(() => {
    const list = query.data?.entitlements ?? []
    if (!providerFilter.trim()) return list
    return list.filter((e) =>
      e.provider.toLowerCase().includes(providerFilter.trim().toLowerCase()),
    )
  }, [query.data, providerFilter])

  const paged = filtered.slice(page * rowsPerPage, page * rowsPerPage + rowsPerPage)

  function onSearch(event: FormEvent) {
    event.preventDefault()
    const next = draft.trim()
    setPage(0)
    setParams(next ? { customerId: next } : {})
  }

  return (
    <>
      <PageHeader
        title="Customer entitlement search"
        subtitle="Fan-out aggregation across CloudShield and NetworkGuard. Partial results are shown when a provider fails."
        actions={
          <Button
            variant="contained"
            disabled={cart.count === 0}
            component={RouterLink}
            to="/extensions/bulk"
          >
            Bulk selection ({cart.count})
          </Button>
        }
      />

      <Stack
        component="form"
        onSubmit={onSearch}
        direction={{ xs: 'column', sm: 'row' }}
        spacing={1.5}
        sx={{ mb: 2 }}
      >
        <TextField
          label="Customer ID"
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
          required
          helperText="Try CUST-1001 from demo seeds"
          inputProps={{ 'aria-label': 'Customer ID' }}
          sx={{ maxWidth: 360 }}
        />
        <Button type="submit" variant="contained" sx={{ alignSelf: { sm: 'flex-start' }, mt: { sm: 1 } }}>
          Search
        </Button>
      </Stack>

      <ErrorAlert error={query.error} title="Search failed" />
      {query.isFetching && <LoadingState label="Aggregating entitlements…" />}

      {query.data && (
        <>
          {query.data.isPartial && (
            <Alert severity="warning" sx={{ mb: 2 }}>
              Partial aggregation — one or more providers failed. Review provider status below.
            </Alert>
          )}

          <Paper variant="outlined" sx={{ p: 2, mb: 2 }}>
            <Typography variant="subtitle2" gutterBottom>
              Provider contributions
            </Typography>
            <Stack direction="row" spacing={2} flexWrap="wrap" useFlexGap>
              {query.data.providers.map((p) => (
                <Stack key={p.provider} spacing={0.5} sx={{ minWidth: 160 }}>
                  <Typography fontWeight={600}>{p.provider}</Typography>
                  <StatusChip status={p.succeeded ? 'Succeeded' : 'Failed'} />
                  <Typography variant="caption" color="text.secondary">
                    {p.entitlementCount} entitlements · {p.duration}
                    {p.error ? ` · ${p.error}` : ''}
                  </Typography>
                </Stack>
              ))}
            </Stack>
          </Paper>

          <TextField
            label="Filter by provider"
            value={providerFilter}
            onChange={(e) => {
              setProviderFilter(e.target.value)
              setPage(0)
            }}
            size="small"
            sx={{ mb: 2, maxWidth: 280 }}
          />

          <Paper variant="outlined">
            <Table aria-label="Entitlements">
              <TableHead>
                <TableRow>
                  <TableCell padding="checkbox">Select</TableCell>
                  <TableCell>Entitlement</TableCell>
                  <TableCell>Provider</TableCell>
                  <TableCell>Product</TableCell>
                  <TableCell>Expiry (UTC)</TableCell>
                  <TableCell>Details</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {paged.map((row) => {
                  const selected = cart.isSelected(row)
                  return (
                    <TableRow key={`${row.provider}:${row.entitlementId}`} hover selected={selected}>
                      <TableCell padding="checkbox">
                        <Checkbox
                          checked={selected}
                          onChange={() => cart.toggle(row)}
                          inputProps={{
                            'aria-label': `Select ${row.displayName}`,
                          }}
                        />
                      </TableCell>
                      <TableCell>
                        <Typography fontWeight={600}>{row.displayName}</Typography>
                        <Typography variant="caption" sx={{ fontFamily: 'IBM Plex Mono, monospace' }}>
                          {row.entitlementId}
                        </Typography>
                      </TableCell>
                      <TableCell>{row.provider}</TableCell>
                      <TableCell>
                        {row.productCode}
                        {row.productGroup ? ` / ${row.productGroup}` : ''}
                      </TableCell>
                      <TableCell>{new Date(row.expiryUtc).toISOString()}</TableCell>
                      <TableCell>
                        <Button
                          size="small"
                          onClick={() =>
                            navigate(
                              `/entitlements/${encodeURIComponent(row.provider)}/${encodeURIComponent(row.entitlementId)}?customerId=${encodeURIComponent(customerId)}`,
                            )
                          }
                        >
                          Open
                        </Button>
                      </TableCell>
                    </TableRow>
                  )
                })}
                {paged.length === 0 && (
                  <TableRow>
                    <TableCell colSpan={6}>
                      <Typography color="text.secondary">No entitlements match the filter.</Typography>
                    </TableCell>
                  </TableRow>
                )}
              </TableBody>
            </Table>
            <TablePagination
              component="div"
              count={filtered.length}
              page={page}
              onPageChange={(_, next) => setPage(next)}
              rowsPerPage={rowsPerPage}
              onRowsPerPageChange={(e) => {
                setRowsPerPage(parseInt(e.target.value, 10))
                setPage(0)
              }}
              rowsPerPageOptions={[5, 10, 25]}
            />
          </Paper>

          <FormControlLabel
            sx={{ mt: 1 }}
            control={
              <Checkbox
                checked={filtered.length > 0 && filtered.every((e) => cart.isSelected(e))}
                indeterminate={
                  filtered.some((e) => cart.isSelected(e)) &&
                  !filtered.every((e) => cart.isSelected(e))
                }
                onChange={(_, checked) => {
                  filtered.forEach((e) => {
                    const selected = cart.isSelected(e)
                    if (checked && !selected) cart.toggle(e)
                    if (!checked && selected) cart.toggle(e)
                  })
                }}
              />
            }
            label="Select all on this filter"
          />
        </>
      )}
    </>
  )
}

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
import { Link as RouterLink } from 'react-router-dom'
import { PageHeader } from '../components/PageHeader'
import { useSelectionCart } from '../hooks/useSelectionCart'

export function BulkSelectionPage() {
  const cart = useSelectionCart()

  return (
    <>
      <PageHeader
        title="Bulk entitlement selection"
        subtitle="Review selected entitlements before creating an extension request (max 1000 items)."
        actions={
          <>
            <Button onClick={cart.clear} disabled={cart.count === 0}>
              Clear
            </Button>
            <Button
              component={RouterLink}
              to="/extensions/new"
              variant="contained"
              disabled={cart.count === 0}
            >
              Continue to create
            </Button>
          </>
        }
      />

      {cart.count === 0 ? (
        <Alert severity="info">
          No entitlements selected.{' '}
          <Button component={RouterLink} to="/entitlements" size="small">
            Search entitlements
          </Button>
        </Alert>
      ) : (
        <Paper variant="outlined">
          <Table aria-label="Selected entitlements">
            <TableHead>
              <TableRow>
                <TableCell>Entitlement</TableCell>
                <TableCell>Provider</TableCell>
                <TableCell>Customer</TableCell>
                <TableCell>Current expiry</TableCell>
                <TableCell>Remove</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {cart.items.map((row) => (
                <TableRow key={`${row.provider}:${row.entitlementId}`}>
                  <TableCell>
                    <Typography fontWeight={600}>{row.displayName}</Typography>
                    <Typography variant="caption" sx={{ fontFamily: 'IBM Plex Mono, monospace' }}>
                      {row.entitlementId}
                    </Typography>
                  </TableCell>
                  <TableCell>{row.provider}</TableCell>
                  <TableCell>{row.customerId}</TableCell>
                  <TableCell>{new Date(row.expiryUtc).toISOString()}</TableCell>
                  <TableCell>
                    <Button size="small" onClick={() => cart.toggle(row)}>
                      Remove
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          <Stack sx={{ p: 2 }}>
            <Typography variant="body2" color="text.secondary">
              {cart.count} entitlement{cart.count === 1 ? '' : 's'} selected
            </Typography>
          </Stack>
        </Paper>
      )}
    </>
  )
}

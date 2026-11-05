import Button from '@mui/material/Button'
import Grid from '@mui/material/Grid'
import Paper from '@mui/material/Paper'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { Link as RouterLink } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { PageHeader } from '../components/PageHeader'

const LINKS = [
  {
    title: 'Search entitlements',
    body: 'Aggregate CloudShield and NetworkGuard coverage for a customer.',
    to: '/entitlements',
  },
  {
    title: 'Create extension request',
    body: 'Select entitlements in bulk and submit a license-extension request.',
    to: '/extensions/new',
  },
  {
    title: 'Approval queues',
    body: 'Renewal Manager and Deal Desk pending decisions.',
    to: '/approvals/renewal-manager',
  },
  {
    title: 'Fulfillment & retries',
    body: 'Track saga progress, inspect failures, and selectively retry.',
    to: '/fulfillment',
  },
]

export function DashboardPage() {
  const { user } = useAuth()

  return (
    <>
      <PageHeader
        title="Dashboard"
        subtitle={`Signed in as ${user?.displayName} (${user?.role}). Use the navigation to manage entitlements, approvals, fulfillment, and ops.`}
      />

      <Grid container spacing={2}>
        {LINKS.map((item) => (
          <Grid key={item.to} size={{ xs: 12, md: 6 }}>
            <Paper
              variant="outlined"
              sx={{
                p: 2.5,
                height: '100%',
                background:
                  'linear-gradient(180deg, rgba(15,110,86,0.05), transparent 70%)',
              }}
            >
              <Stack spacing={1.5} alignItems="flex-start">
                <Typography variant="h6">{item.title}</Typography>
                <Typography color="text.secondary">{item.body}</Typography>
                <Button component={RouterLink} to={item.to} variant="outlined">
                  Open
                </Button>
              </Stack>
            </Paper>
          </Grid>
        ))}
      </Grid>
    </>
  )
}

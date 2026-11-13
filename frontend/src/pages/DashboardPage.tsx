import Button from '@mui/material/Button'
import Grid from '@mui/material/Grid'
import Paper from '@mui/material/Paper'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { Link as RouterLink } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import type { EntitleXRole } from '../api/types'
import { PageHeader } from '../components/PageHeader'

const LINKS: Array<{
  title: string
  body: string
  to: string
  roles?: EntitleXRole[]
}> = [
  {
    title: 'Search entitlements',
    body: 'Aggregate CloudShield and NetworkGuard coverage for a customer.',
    to: '/entitlements',
    roles: ['RenewalRepresentative', 'DealDeskManager', 'PlatformAdmin'],
  },
  {
    title: 'Create extension request',
    body: 'Select entitlements in bulk and submit a license-extension request.',
    to: '/extensions/new',
    roles: ['RenewalRepresentative', 'DealDeskManager', 'PlatformAdmin'],
  },
  {
    title: 'Approval queues',
    body: 'Renewal Manager and Deal Desk pending decisions.',
    to: '/approvals/renewal-manager',
    roles: ['RenewalManager', 'PlatformAdmin'],
  },
  {
    title: 'Deal Desk queue',
    body: 'Pending Deal Desk approval decisions.',
    to: '/approvals/deal-desk',
    roles: ['DealDeskManager'],
  },
  {
    title: 'Fulfillment & retries',
    body: 'Track saga progress, inspect failures, and selectively retry.',
    to: '/fulfillment',
  },
]

export function DashboardPage() {
  const { user, hasRole } = useAuth()
  const links = LINKS.filter((item) => !item.roles || hasRole(...item.roles))

  return (
    <>
      <PageHeader
        title="Dashboard"
        subtitle={`Signed in as ${user?.displayName} (${user?.role}). Use the navigation to manage entitlements, approvals, fulfillment, and ops.`}
      />

      <Grid container spacing={2}>
        {links.map((item) => (
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

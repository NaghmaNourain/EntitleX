import Chip from '@mui/material/Chip'

const SUCCESS = new Set(['Succeeded', 'Approved', 'Completed', 'Healthy', 'AutoApprove'])
const WARN = new Set([
  'Pending',
  'PendingApproval',
  'InFulfillment',
  'QueuedForFulfillment',
  'Processing',
  'Uncertain',
  'ReconciliationRequired',
  'Partial',
])
const ERROR = new Set(['Failed', 'Rejected', 'Unsupported', 'Escalated', 'TimedOut'])

function tone(status: string): 'success' | 'warning' | 'error' | 'default' {
  if (SUCCESS.has(status)) return 'success'
  if (WARN.has(status)) return 'warning'
  if (ERROR.has(status)) return 'error'
  return 'default'
}

export function StatusChip({ status, label }: { status: string; label?: string }) {
  return (
    <Chip
      size="small"
      label={label ?? status}
      color={tone(status)}
      variant={tone(status) === 'default' ? 'outlined' : 'filled'}
      sx={{ fontFamily: '"IBM Plex Mono", monospace', fontWeight: 500 }}
    />
  )
}

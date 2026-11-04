import Box from '@mui/material/Box'
import CircularProgress from '@mui/material/CircularProgress'
import Typography from '@mui/material/Typography'

export function LoadingState({ label = 'Loading…' }: { label?: string }) {
  return (
    <Box
      role="status"
      aria-live="polite"
      aria-busy="true"
      sx={{
        display: 'flex',
        alignItems: 'center',
        gap: 2,
        py: 4,
        justifyContent: 'center',
      }}
    >
      <CircularProgress size={28} aria-hidden />
      <Typography color="text.secondary">{label}</Typography>
    </Box>
  )
}

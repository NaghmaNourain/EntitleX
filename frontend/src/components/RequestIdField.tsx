import Button from '@mui/material/Button'
import Stack from '@mui/material/Stack'
import TextField from '@mui/material/TextField'
import { useState, type FormEvent } from 'react'

export function RequestIdField({
  label = 'Request ID',
  initial = '',
  onSubmit,
  buttonLabel = 'Load',
}: {
  label?: string
  initial?: string
  buttonLabel?: string
  onSubmit: (requestId: string) => void
}) {
  const [value, setValue] = useState(initial)
  const [error, setError] = useState<string | null>(null)

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    const trimmed = value.trim()
    if (!/^[0-9a-fA-F-]{36}$/.test(trimmed)) {
      setError('Enter a valid GUID request id')
      return
    }
    setError(null)
    onSubmit(trimmed)
  }

  return (
    <Stack
      component="form"
      onSubmit={handleSubmit}
      direction={{ xs: 'column', sm: 'row' }}
      spacing={1.5}
      alignItems={{ sm: 'flex-start' }}
      sx={{ mb: 3 }}
    >
      <TextField
        label={label}
        value={value}
        onChange={(e) => setValue(e.target.value)}
        error={Boolean(error)}
        helperText={error ?? 'UUID of the license-extension request'}
        fullWidth
        required
        inputProps={{ 'aria-label': label, spellCheck: false }}
        sx={{ maxWidth: 520, fontFamily: 'IBM Plex Mono, monospace' }}
      />
      <Button type="submit" variant="contained" sx={{ mt: { sm: 1 } }}>
        {buttonLabel}
      </Button>
    </Stack>
  )
}

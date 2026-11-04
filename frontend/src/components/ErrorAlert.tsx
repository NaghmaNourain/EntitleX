import Alert from '@mui/material/Alert'
import AlertTitle from '@mui/material/AlertTitle'
import List from '@mui/material/List'
import ListItem from '@mui/material/ListItem'
import ListItemText from '@mui/material/ListItemText'
import { ApiError, formatValidationErrors } from '../api/client'

export function ErrorAlert({ error, title = 'Something went wrong' }: { error: unknown; title?: string }) {
  if (!error) return null

  if (error instanceof ApiError) {
    const validation = formatValidationErrors(error.problem)
    return (
      <Alert severity="error" role="alert" sx={{ mb: 2 }}>
        <AlertTitle>
          {title} ({error.status})
        </AlertTitle>
        {error.message}
        {validation.length > 0 && (
          <List dense disablePadding sx={{ mt: 1 }}>
            {validation.map((item) => (
              <ListItem key={item} disableGutters>
                <ListItemText primary={item} />
              </ListItem>
            ))}
          </List>
        )}
      </Alert>
    )
  }

  const message = error instanceof Error ? error.message : 'Unexpected error'
  return (
    <Alert severity="error" role="alert" sx={{ mb: 2 }}>
      <AlertTitle>{title}</AlertTitle>
      {message}
    </Alert>
  )
}

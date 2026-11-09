import Alert from '@mui/material/Alert'
import Button from '@mui/material/Button'
import Chip from '@mui/material/Chip'
import List from '@mui/material/List'
import ListItem from '@mui/material/ListItem'
import ListItemText from '@mui/material/ListItemText'
import Paper from '@mui/material/Paper'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { api } from '../api/endpoints'
import type { RequestInsight } from '../api/types'
import { ErrorAlert } from './ErrorAlert'
import { LoadingState } from './LoadingState'

type Mode = 'explain' | 'rca'

export function RequestInsightPanel({
  requestId,
  mode,
  title,
  buttonLabel,
}: {
  requestId: string
  mode: Mode
  title: string
  buttonLabel: string
}) {
  const [enabled, setEnabled] = useState(false)

  const query = useQuery({
    queryKey: ['insight', mode, requestId],
    queryFn: ({ signal }) =>
      mode === 'rca'
        ? api.reconcileRca(requestId, signal)
        : api.explainRequest(requestId, signal),
    enabled: Boolean(requestId) && enabled,
    retry: false,
  })

  return (
    <Paper variant="outlined" sx={{ p: 2 }}>
      <Stack spacing={1.5}>
        <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
          <Typography variant="h6">{title}</Typography>
          <Button
            size="small"
            variant="outlined"
            disabled={!requestId || query.isFetching}
            onClick={() => setEnabled(true)}
          >
            {query.isFetching ? 'Loading…' : buttonLabel}
          </Button>
          {query.data && (
            <Chip
              size="small"
              label={query.data.narrationMode}
              color={query.data.narrationMode === 'Llm' ? 'primary' : 'default'}
            />
          )}
        </Stack>

        <Typography variant="body2" color="text.secondary">
          Grounded from request, approval, saga, audit, ledger, and poison outbox. Optional local
          Ollama may rephrase the summary; recommended action stays deterministic. No writes.
        </Typography>

        {enabled && query.isLoading && <LoadingState />}
        <ErrorAlert error={query.error} title={`${title} failed`} />
        {query.data && <InsightBody insight={query.data} />}
      </Stack>
    </Paper>
  )
}

function InsightBody({ insight }: { insight: RequestInsight }) {
  return (
    <Stack spacing={1.5}>
      <Alert severity="info">{insight.summary}</Alert>
      <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
        <Chip label={`Action: ${insight.recommendedAction}`} color="warning" variant="outlined" />
        {insight.model && <Chip size="small" label={insight.model} variant="outlined" />}
        <Chip
          size="small"
          label={`Uncertain ${insight.facts.uncertainItemCount} · Poison ${insight.facts.poisonOutboxCount} · Mismatch ${insight.facts.ledgerMismatchCount}`}
          variant="outlined"
        />
      </Stack>
      {insight.citations.length > 0 && (
        <>
          <Typography variant="subtitle2">Citations</Typography>
          <List dense disablePadding>
            {insight.citations.slice(0, 12).map((c) => (
              <ListItem key={`${c.kind}-${c.id}`} disableGutters sx={{ py: 0.25 }}>
                <ListItemText
                  primary={`${c.kind}: ${c.label}`}
                  secondary={c.id}
                  primaryTypographyProps={{ variant: 'body2' }}
                  secondaryTypographyProps={{
                    variant: 'caption',
                    sx: { fontFamily: 'IBM Plex Mono, monospace' },
                  }}
                />
              </ListItem>
            ))}
          </List>
        </>
      )}
    </Stack>
  )
}

import CssBaseline from '@mui/material/CssBaseline'
import { ThemeProvider } from '@mui/material/styles'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, type RenderOptions } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import React, { type ReactElement, type ReactNode } from 'react'
import { AuthProvider } from '../auth/AuthContext'
import { entitlexTheme } from '../theme/theme'

export function createTestQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  })
}

export function renderWithProviders(
  ui: ReactElement,
  options?: Omit<RenderOptions, 'wrapper'> & { route?: string },
) {
  const client = createTestQueryClient()
  const route = options?.route ?? '/'

  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <ThemeProvider theme={entitlexTheme}>
        <CssBaseline />
        <QueryClientProvider client={client}>
          <MemoryRouter initialEntries={[route]}>
            <AuthProvider>{children}</AuthProvider>
          </MemoryRouter>
        </QueryClientProvider>
      </ThemeProvider>
    )
  }

  return {
    client,
    ...render(ui, { wrapper: Wrapper, ...options }),
  }
}

import { screen, waitFor } from '@testing-library/react'
import { Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it } from 'vitest'
import { RequireAuth } from './RequireAuth'
import { renderWithProviders } from '../test/test-utils'

describe('RequireAuth', () => {
  beforeEach(() => {
    sessionStorage.clear()
    localStorage.clear()
  })

  it('shows a loading status while auth bootstraps then redirects unauthenticated users', async () => {
    renderWithProviders(
      <Routes>
        <Route
          path="/"
          element={
            <RequireAuth>
              <div>Private</div>
            </RequireAuth>
          }
        />
        <Route path="/login" element={<div>Login gate</div>} />
      </Routes>,
      { route: '/' },
    )

    expect(screen.getByRole('status')).toHaveTextContent(/Checking session/i)

    await waitFor(() => {
      expect(screen.getByText('Login gate')).toBeInTheDocument()
    })
  })
})

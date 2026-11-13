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

  it('redirects unauthenticated users to login after auth bootstrap', async () => {
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

    // Bootstrap may resolve before the first sync assert; wait for the login gate.
    await waitFor(() => {
      expect(screen.getByText('Login gate')).toBeInTheDocument()
    })
    expect(screen.queryByText('Private')).not.toBeInTheDocument()
  })
})

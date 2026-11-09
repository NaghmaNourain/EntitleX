import { screen } from '@testing-library/react'
import { describe, expect, it, vi, beforeEach } from 'vitest'
import { axe } from 'vitest-axe'
import { LoginPage } from './LoginPage'
import { renderWithProviders } from '../test/test-utils'

vi.mock('../auth/config', () => ({
  authConfig: {
    oidcEnabled: false,
    authority: '',
    clientId: '',
    audience: '',
    enableDevToken: true,
    redirectPath: '/login/callback',
    postLogoutPath: '/login',
  },
}))

describe('LoginPage', () => {
  beforeEach(() => {
    sessionStorage.clear()
    localStorage.clear()
  })

  it('renders brand and DevToken form controls', async () => {
    renderWithProviders(<LoginPage />, { route: '/login' })
    expect(screen.getByRole('heading', { name: 'EntitleX' })).toBeInTheDocument()
    expect(screen.getByLabelText(/Role/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Continue with DevToken/i })).toBeInTheDocument()
  })

  it('has no critical axe violations', async () => {
    const { container } = renderWithProviders(<LoginPage />, { route: '/login' })
    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})

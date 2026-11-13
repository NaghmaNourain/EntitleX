import React from 'react'
import { screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { axe } from 'vitest-axe'
import { ApprovalQueuePage } from './ApprovalQueuePage'
import { renderWithProviders } from '../test/test-utils'
import type { PendingApprovalsPage } from '../api/types'

const listPendingApprovals = vi.fn()

vi.mock('../api/endpoints', () => ({
  api: {
    listPendingApprovals: (...args: unknown[]) => listPendingApprovals(...args),
    decideApproval: vi.fn(),
  },
}))

vi.mock('../auth/AuthContext', async () => {
  const actual = await vi.importActual<typeof import('../auth/AuthContext')>('../auth/AuthContext')
  return {
    ...actual,
    useAuth: () => ({
      token: 'test-token',
      user: {
        userId: 'user-rm-001',
        displayName: 'Morgan Manager',
        email: 'renewal.manager@entitlex.local',
        role: 'RenewalManager' as const,
        roles: ['RenewalManager' as const],
      },
      isAuthenticated: true,
      isLoading: false,
      oidcEnabled: false,
      enableDevToken: true,
      loginWithOidc: async () => undefined,
      completeOidcCallback: async () => undefined,
      loginWithRole: async () => undefined,
      logout: async () => undefined,
      hasRole: () => true,
    }),
  }
})

describe('ApprovalQueuePage', () => {
  beforeEach(() => {
    listPendingApprovals.mockReset()
  })

  it('shows loading then empty queue state', async () => {
    const page: PendingApprovalsPage = {
      total: 0,
      skip: 0,
      take: 10,
      items: [],
      page: 1,
      pageSize: 10,
      totalCount: 0,
    }
    listPendingApprovals.mockResolvedValue(page)

    renderWithProviders(<ApprovalQueuePage role="RenewalManager" title="RM queue" />)

    expect(screen.getByRole('status')).toBeInTheDocument()
    await waitFor(() => {
      expect(screen.getByText(/Queue is empty/i)).toBeInTheDocument()
    })
  })

  it('renders error alert when the list query fails', async () => {
    listPendingApprovals.mockRejectedValue(new Error('backend unavailable'))

    renderWithProviders(<ApprovalQueuePage role="RenewalManager" title="RM queue" />)

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(/backend unavailable/i)
    })
  })

  it('passes axe smoke for the empty list page', async () => {
    listPendingApprovals.mockResolvedValue({
      total: 0,
      skip: 0,
      take: 10,
      items: [],
      page: 1,
      pageSize: 10,
      totalCount: 0,
    } satisfies PendingApprovalsPage)

    const { container } = renderWithProviders(
      <ApprovalQueuePage role="RenewalManager" title="RM queue" />,
    )

    await waitFor(() => {
      expect(screen.getByText(/Queue is empty/i)).toBeInTheDocument()
    })

    const results = await axe(container)
    expect(results.violations).toEqual([])
  })
})

import { apiRequest } from './client'
import type {
  ApprovalCase,
  CreateLicenseExtensionRequestBody,
  CustomerEntitlementAggregation,
  DevTokenResponse,
  EntitleXRole,
  HealthStatus,
  LicenseExtensionRequest,
  OpsSummary,
  PendingApprovalsPage,
  ReconciliationRunResult,
  RequestAuditPage,
  SagaDto,
} from './types'

export const api = {
  mintDevToken(role: EntitleXRole) {
    return apiRequest<DevTokenResponse>('/api/v1/dev/tokens', {
      method: 'POST',
      body: { role },
      auth: false,
    })
  },

  searchEntitlements(customerId: string, signal?: AbortSignal) {
    const q = new URLSearchParams({ customerId })
    return apiRequest<CustomerEntitlementAggregation>(
      `/api/v1/entitlements/search?${q.toString()}`,
      { signal },
    )
  },

  createLicenseExtensionRequest(body: CreateLicenseExtensionRequestBody) {
    return apiRequest<LicenseExtensionRequest>('/api/v1/license-extension-requests', {
      method: 'POST',
      body,
    })
  },

  getLicenseExtensionRequest(id: string, signal?: AbortSignal) {
    return apiRequest<LicenseExtensionRequest>(`/api/v1/license-extension-requests/${id}`, {
      signal,
    })
  },

  startApproval(requestId: string) {
    return apiRequest<ApprovalCase>(
      `/api/v1/license-extension-requests/${requestId}/approval/start`,
      { method: 'POST' },
    )
  },

  getApproval(requestId: string, signal?: AbortSignal) {
    return apiRequest<ApprovalCase>(
      `/api/v1/license-extension-requests/${requestId}/approval`,
      { signal },
    )
  },

  decideApproval(requestId: string, outcome: 'Approved' | 'Rejected', comment?: string) {
    return apiRequest<ApprovalCase>(
      `/api/v1/license-extension-requests/${requestId}/approval/decisions`,
      {
        method: 'POST',
        body: { outcome, comment },
      },
    )
  },

  listPendingApprovals(params: { role?: string; take?: number; skip?: number }, signal?: AbortSignal) {
    const q = new URLSearchParams()
    if (params.role) q.set('role', params.role)
    if (params.take != null) q.set('take', String(params.take))
    if (params.skip != null) q.set('skip', String(params.skip))
    const qs = q.toString()
    return apiRequest<PendingApprovalsPage>(
      `/api/v1/approvals/pending${qs ? `?${qs}` : ''}`,
      { signal },
    )
  },

  getSaga(requestId: string, signal?: AbortSignal) {
    return apiRequest<SagaDto>(
      `/api/v1/license-extension-requests/${requestId}/saga`,
      { signal },
    )
  },

  retryFailed(
    requestId: string,
    body: {
      notes?: string
      logicalOperationIds?: string[]
      idempotencyKey?: string
    },
    idempotencyKey: string,
  ) {
    return apiRequest<LicenseExtensionRequest>(
      `/api/v1/license-extensions/${requestId}/retry-failed`,
      {
        method: 'POST',
        body,
        idempotencyKey,
      },
    )
  },

  getAudit(requestId: string, signal?: AbortSignal) {
    return apiRequest<RequestAuditPage>(`/api/v1/audit/requests/${requestId}`, { signal })
  },

  getOpsSummary(take = 25, signal?: AbortSignal) {
    return apiRequest<OpsSummary>(`/api/v1/admin/ops/summary?take=${take}`, { signal })
  },

  manualReconcile(requestId: string) {
    return apiRequest<ReconciliationRunResult>(
      `/api/v1/admin/reconciliation/requests/${requestId}`,
      { method: 'POST' },
    )
  },

  getHealth(signal?: AbortSignal) {
    return apiRequest<HealthStatus>('/health', { auth: false, signal })
  },

  getReady(signal?: AbortSignal) {
    return apiRequest<unknown>('/health/ready', { auth: false, signal })
  },

  getProviderHealth(signal?: AbortSignal) {
    return apiRequest<unknown>('/health/providers', { auth: false, signal })
  },
}

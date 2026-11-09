export type EntitleXRole =
  | 'RenewalRepresentative'
  | 'RenewalManager'
  | 'DealDeskManager'
  | 'PlatformAdmin'

export interface DevTokenResponse {
  accessToken: string
  tokenType: string
  expiresInMinutes: number
  user: {
    userId: string
    displayName: string
    email: string
    role: EntitleXRole
  }
  usage: string
}

export interface AuthUser {
  userId: string
  displayName: string
  email: string
  /** Primary role used for UI affordances (first EntitleX role on the token). */
  role: EntitleXRole
  /** All EntitleX roles present on the token (OIDC may grant more than one). */
  roles?: EntitleXRole[]
}

export interface NormalizedEntitlement {
  entitlementId: string
  provider: string
  customerId: string
  productCode: string
  productGroup: string | null
  displayName: string
  expiryUtc: string
  updatedAtUtc: string | null
}

export interface ProviderContribution {
  provider: string
  succeeded: boolean
  entitlementCount: number
  error: string | null
  duration: string
}

export interface CustomerEntitlementAggregation {
  customerId: string
  correlationId: string
  entitlements: NormalizedEntitlement[]
  providers: ProviderContribution[]
  isPartial: boolean
}

export interface LicenseExtensionRequestItem {
  id: string
  entitlementId: string
  provider: string
  logicalOperationId: string
  attemptId: string
  currentExpiryUtc: string
  requestedExpiryUtc: string
}

export interface RetryHistoryEntry {
  id: string
  retryRequestId: string
  extensionNumber: string
  itemCount: number
  createdBy: string
  notes: string | null
  createdAtUtc: string
}

export interface LicenseExtensionRequest {
  id: string
  extensionNumber: string
  parentId: string | null
  status: string
  approvalVersion: number
  approvedScopeHash: string | null
  requestedBy: string
  createdAtUtc: string
  notes: string | null
  items: LicenseExtensionRequestItem[]
  retryHistory: RetryHistoryEntry[]
}

export interface CreateLicenseExtensionRequestItem {
  entitlementId: string
  provider: string
  currentExpiryUtc: string
  requestedExpiryUtc: string
}

export interface CreateLicenseExtensionRequestBody {
  requestedBy: string
  extensionNumber?: string | null
  items: CreateLicenseExtensionRequestItem[]
}

export interface ApprovalStage {
  id: string
  order: number
  requiredRole: string
  status: string
  decisionId: string | null
}

export interface ApprovalDecision {
  id: string
  stageOrder: number
  stageRole: string
  outcome: string
  actorUserId: string
  actorRole: string
  actorDisplayName: string
  comment: string | null
  correlationId: string | null
  decidedAtUtc: string
}

export interface ApprovalCase {
  id: string
  requestId: string
  maxExtensionDays: number
  band: string
  status: string
  currentStageOrder: number
  createdAtUtc: string
  completedAtUtc: string | null
  stages: ApprovalStage[]
  decisions: ApprovalDecision[]
}

export interface PendingApprovalSummary {
  approvalCaseId: string
  requestId: string
  maxExtensionDays: number
  band: string
  status: string
  currentStageOrder: number
  currentStageRole: string | null
  createdAtUtc: string
}

export interface PendingApprovalsPage {
  total: number
  skip: number
  take: number
  items: PendingApprovalSummary[]
}

export interface SagaTransition {
  id: string
  fromState: string
  toState: string
  reason: string
  actor: string | null
  correlationId: string | null
  occurredAtUtc: string
}

export interface SagaItemOutcome {
  id: string
  requestItemId: string
  entitlementId: string
  logicalOperationId: string
  status: string
  errorCode: string | null
  errorMessage: string | null
  updatedAtUtc: string
}

export interface SagaDto {
  id: string
  requestId: string
  state: string
  requestStatusProjection: string
  approvalBand: string
  requiresDealDesk: boolean
  renewalManagerApproved: boolean
  dealDeskApproved: boolean
  timeoutKind: string
  timeoutDeadlineUtc: string | null
  createdAtUtc: string
  updatedAtUtc: string
  succeededItemCount: number
  failedItemCount: number
  pendingItemCount: number
  transitions: SagaTransition[]
  itemOutcomes: SagaItemOutcome[]
}

export interface AuditEvent {
  id: string
  eventType: string
  requestId: string | null
  logicalOperationId: string | null
  actor: string
  detail: string
  correlationId: string | null
  occurredAtUtc: string
}

export interface RequestAuditPage {
  requestId: string
  events: AuditEvent[]
}

export interface OpsSummary {
  generatedAtUtc: string
  pendingApprovalCount: number
  reconciliationRequiredCount: number
  pendingApprovals: Array<{
    requestId: string
    band: string
    currentStageRole: string | null
    createdAtUtc: string
  }>
  reconciliationRequired: Array<{
    requestId: string
    sagaState: string
    succeededItemCount: number
    failedItemCount: number
    pendingItemCount: number
    updatedAtUtc: string
  }>
}

export interface ReconciliationRunResult {
  requestId: string
  sagaState: string | number
  items: Array<{
    logicalOperationId: string
    provider: string
    action: string | number
    detail: string
  }>
  stillRequiresReconciliation: boolean
}

export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  instance?: string
  errors?: Record<string, string[]>
}

export interface HealthStatus {
  status: string
  service?: string
  timestampUtc?: string
}

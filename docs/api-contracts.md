# API contracts

Authoritative inventory of HTTP endpoints as of Phase 12. Auth policies live in `AuthorizationPolicies` (`AuthenticationServiceCollectionExtensions`). Roles: `EntitleXRoles`.

## Auth policies

| Policy | Roles |
|--------|-------|
| `CanCreateRequests` | `RenewalRepresentative`, `PlatformAdmin` |
| `CanDecideApprovals` | `RenewalManager`, `DealDeskManager`, `PlatformAdmin` |
| `CanReadApprovals` | any authenticated user |
| `CanManageReconciliation` | `PlatformAdmin` |
| Default `RequireAuthorization()` | authenticated |

Simulators (**CloudShield**, **NetworkGuard**) use **`X-Api-Key`** (`SimulatorAuth:ApiKey`), not JWT. Bound to `127.0.0.1` in lab.

---

## EntitleX.Api (`:5080`)

Host: `src/EntitleX.Api` — JWT Bearer (OIDC Authority and/or Dev HMAC); Swagger in Development.

OIDC (generic): `Oidc:Enabled`, `Oidc:Authority`, `Oidc:Audience`, `Oidc:RoleClaimType`, optional `Oidc:RoleMap`. Production requires Authority; DevToken is forced off. Policies above are unchanged.

### Health / ops infra

| Method | Path | Auth | Notes |
|--------|------|------|-------|
| `GET` | `/health` | Anonymous | Liveness |
| `GET` | `/health/ready` | Anonymous | ASP.NET health checks |
| `GET` | `/health/providers` | Anonymous | Provider-tagged checks |
| `GET` | `/metrics` | Scraping | Prometheus |

### Dev tokens

| Method | Path | Auth | Handler |
|--------|------|------|---------|
| `POST` | `/api/v1/dev/tokens` | Anonymous; **Development only** when `Jwt:EnableDevTokenEndpoint` (disabled in Production) | `DevTokenEndpoints.MintAsync` |

Body: `{ "role": "RenewalRepresentative" | "RenewalManager" | "DealDeskManager" | "PlatformAdmin" }`.
`PlatformAdmin` requires `Jwt:AllowDevPlatformAdmin=true` (default false). Prefer Keycloak OIDC locally when Compose Keycloak is up.

### License extension requests

| Method | Path | Auth | Handler |
|--------|------|------|---------|
| `POST` | `/api/v1/license-extension-requests/` | `CanCreateRequests` | `LicenseExtensionEndpoints.CreateAsync` → `CreateLicenseExtensionRequestCommand` (`RequestedBy` bound from JWT; client actor ignored) |
| `GET` | `/api/v1/license-extension-requests/{id}` | Authenticated + resource auth | Owner, or RenewalManager / DealDeskManager / PlatformAdmin |
| `POST` | `/api/v1/license-extensions/{requestId}/retry-failed` | `CanCreateRequests`; requires `Idempotency-Key` (header or body) | `LicenseExtensionRetryEndpoints.RetryFailedAsync` → `RetryFailedLicenseExtensionsCommand` |

Retry body (optional filter):

```json
{
  "notes": "optional",
  "logicalOperationIds": ["guid", "..."]
}
```

Returns `201 Created` with new request DTO (`Location` → `/api/v1/license-extension-requests/{newId}`). Same idempotency key → prior retry (lost-response replay).

### Entitlements

| Method | Path | Auth | Handler |
|--------|------|------|---------|
| `GET` | `/api/v1/entitlements/search?customerId=` | `PlatformAdmin` / `DealDeskManager` | `EntitlementAggregationEndpoints.SearchAsync` → `SearchCustomerEntitlementsQuery` |

Aggregates CloudShield + NetworkGuard; partial results when a provider fails.

### Approvals

| Method | Path | Auth | Handler |
|--------|------|------|---------|
| `POST` | `/api/v1/license-extension-requests/{requestId}/approval/start` | `CanCreateRequests` | `ApprovalEndpoints.StartAsync` → `StartApprovalCommand` |
| `GET` | `/api/v1/license-extension-requests/{requestId}/approval/` | `CanReadApprovals` | `ApprovalEndpoints.GetAsync` → `GetApprovalQuery` |
| `POST` | `/api/v1/license-extension-requests/{requestId}/approval/decisions` | `CanDecideApprovals` | `ApprovalEndpoints.DecideAsync` → `DecideApprovalCommand` |
| `GET` | `/api/v1/approvals/pending` | `CanDecideApprovals` | `PendingApprovalEndpoints.ListPendingAsync` → `ListPendingApprovalsQuery` |

Decide body: `{ "outcome": "Approved" | "Rejected", "comment": "..." }`.

Pending query params: `role`, `take`, `skip`, keyset cursors.

### Saga

| Method | Path | Auth | Handler |
|--------|------|------|---------|
| `GET` | `/api/v1/license-extension-requests/{requestId}/saga/` | `CanReadApprovals` | `SagaEndpoints.GetAsync` → `GetSagaQuery` |

### Admin / audit

| Method | Path | Auth | Handler |
|--------|------|------|---------|
| `POST` | `/api/v1/admin/reconciliation/requests/{requestId}` | `CanManageReconciliation` (+ handler re-checks `PlatformAdmin`) | `ReconciliationEndpoints.ManualReconcileAsync` → `ManualReconcileCommand` |
| `GET` | `/api/v1/admin/ops/summary?take=` | `CanManageReconciliation` | `OpsEndpoints.GetSummaryAsync` → `GetOpsSummaryQuery` |
| `GET` | `/api/v1/audit/requests/{requestId}` | `CanReadApprovals` | `AuditEndpoints.GetByRequestAsync` → `GetRequestAuditQuery` |

Endpoint source files: `src/EntitleX.Api/Endpoints/`.

---

## CloudShield.Api (`127.0.0.1:5081`, `X-Api-Key`)

`src/simulators/CloudShield/CloudShield.Api/Endpoints/EntitlementEndpoints.cs`

| Method | Path |
|--------|------|
| `GET` | `/health`, `/health/ready`, `/metrics` |
| `GET` | `/api/v1/accounts/{customerAccountId}/entitlements` |
| `GET` | `/api/v1/entitlements/{entitlementKey}` |
| `POST` | `/api/v1/entitlements/{entitlementKey}/license-extensions` |
| `GET` | `/api/v1/license-operations/{logicalOperationId}` |

Demo customer: `CUST-1001`.

---

## NetworkGuard.Api (`127.0.0.1:5082`, `X-Api-Key`)

`src/simulators/NetworkGuard/NetworkGuard.Api/Endpoints/LicenseEndpoints.cs`

| Method | Path |
|--------|------|
| `GET` | `/health`, `/health/ready`, `/metrics` |
| `GET` | `/api/v1/tenants/{tenantExternalId}/licenses` |
| `GET` | `/api/v1/licenses/{licenseId}` |
| `POST` | `/api/v1/licenses/{licenseId}/extensions` |
| `GET` | `/api/v1/operations/{logicalOperationId}` |

---

## Related

- [event-contracts.md](event-contracts.md) — async messaging (not HTTP)
- [idempotency.md](idempotency.md) — retry semantics
- [reconciliation.md](reconciliation.md) — admin reconcile

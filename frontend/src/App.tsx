import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import CssBaseline from '@mui/material/CssBaseline'
import { ThemeProvider } from '@mui/material/styles'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import { AuthProvider } from './auth/AuthContext'
import { RequireAuth } from './auth/RequireAuth'
import { AppLayout } from './components/AppLayout'
import { ApprovalQueuePage } from './pages/ApprovalQueuePage'
import { AuditHistoryPage } from './pages/AuditHistoryPage'
import { BulkSelectionPage } from './pages/BulkSelectionPage'
import { CreateExtensionRequestPage } from './pages/CreateExtensionRequestPage'
import { DashboardPage } from './pages/DashboardPage'
import { EntitlementDetailsPage } from './pages/EntitlementDetailsPage'
import { EntitlementSearchPage } from './pages/EntitlementSearchPage'
import { FailedEntitlementDetailsPage } from './pages/FailedEntitlementDetailsPage'
import { FulfillmentProgressPage } from './pages/FulfillmentProgressPage'
import { LoginPage } from './pages/LoginPage'
import { OidcCallbackPage } from './pages/OidcCallbackPage'
import { OpsMonitoringPage } from './pages/OpsMonitoringPage'
import { RequestDetailsPage } from './pages/RequestDetailsPage'
import { RequestLineagePage } from './pages/RequestLineagePage'
import { RetryFailedPage } from './pages/RetryFailedPage'
import { entitlexTheme } from './theme/theme'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 10_000,
      retry: 1,
      refetchOnWindowFocus: false,
    },
  },
})

export default function App() {
  return (
    <ThemeProvider theme={entitlexTheme}>
      <CssBaseline />
      <QueryClientProvider client={queryClient}>
        <AuthProvider>
          <BrowserRouter>
            <Routes>
              <Route path="/login" element={<LoginPage />} />
              <Route path="/login/callback" element={<OidcCallbackPage />} />
              <Route
                element={
                  <RequireAuth>
                    <AppLayout />
                  </RequireAuth>
                }
              >
                <Route index element={<DashboardPage />} />
                <Route
                  path="entitlements"
                  element={
                    <RequireAuth roles={['RenewalRepresentative', 'DealDeskManager', 'PlatformAdmin']}>
                      <EntitlementSearchPage />
                    </RequireAuth>
                  }
                />
                <Route
                  path="entitlements/:provider/:entitlementId"
                  element={
                    <RequireAuth roles={['RenewalRepresentative', 'DealDeskManager', 'PlatformAdmin']}>
                      <EntitlementDetailsPage />
                    </RequireAuth>
                  }
                />
                <Route
                  path="extensions/bulk"
                  element={
                    <RequireAuth roles={['RenewalRepresentative', 'PlatformAdmin']}>
                      <BulkSelectionPage />
                    </RequireAuth>
                  }
                />
                <Route
                  path="extensions/new"
                  element={
                    <RequireAuth roles={['RenewalRepresentative', 'PlatformAdmin']}>
                      <CreateExtensionRequestPage />
                    </RequireAuth>
                  }
                />
                <Route path="requests" element={<RequestDetailsPage />} />
                <Route path="requests/:requestId" element={<RequestDetailsPage />} />
                <Route
                  path="approvals/renewal-manager"
                  element={
                    <RequireAuth roles={['RenewalManager', 'PlatformAdmin']}>
                      <ApprovalQueuePage
                        role="RenewalManager"
                        title="Renewal Manager approval queue"
                      />
                    </RequireAuth>
                  }
                />
                <Route
                  path="approvals/deal-desk"
                  element={
                    <RequireAuth roles={['DealDeskManager', 'PlatformAdmin']}>
                      <ApprovalQueuePage
                        role="DealDeskManager"
                        title="Deal Desk approval queue"
                      />
                    </RequireAuth>
                  }
                />
                <Route path="fulfillment" element={<FulfillmentProgressPage />} />
                <Route path="fulfillment/:requestId" element={<FulfillmentProgressPage />} />
                <Route path="failures" element={<FailedEntitlementDetailsPage />} />
                <Route path="failures/:requestId" element={<FailedEntitlementDetailsPage />} />
                <Route path="retry" element={<RetryFailedPage />} />
                <Route path="retry/:requestId" element={<RetryFailedPage />} />
                <Route path="lineage" element={<RequestLineagePage />} />
                <Route path="lineage/:requestId" element={<RequestLineagePage />} />
                <Route path="audit" element={<AuditHistoryPage />} />
                <Route path="audit/:requestId" element={<AuditHistoryPage />} />
                <Route
                  path="ops"
                  element={
                    <RequireAuth roles={['PlatformAdmin']}>
                      <OpsMonitoringPage />
                    </RequireAuth>
                  }
                />
              </Route>
              <Route path="*" element={<Navigate to="/" replace />} />
            </Routes>
          </BrowserRouter>
        </AuthProvider>
      </QueryClientProvider>
    </ThemeProvider>
  )
}

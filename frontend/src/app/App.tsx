import { QueryCache, QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { lazy, Suspense } from 'react'
import { BrowserRouter, Navigate, Outlet, Route, Routes } from 'react-router-dom'
import { SESSION_KEY, useSession } from '../features/auth/session'
import { ApiError } from '../shared/api/client'
import { SkeletonRows } from '../shared/ui/components'
import { AppShell } from './AppShell'

// Each screen is its own chunk, downloaded when its route is first opened.
const SignInPage = lazy(() => import('../features/auth/SignInPage'))
const DashboardPage = lazy(() => import('../features/dashboard/DashboardPage'))
const HistoryPage = lazy(() => import('../features/history/HistoryPage'))
const CalendarPage = lazy(() => import('../features/calendar/CalendarPage'))
const EmployeesPage = lazy(() => import('../features/employees/EmployeesPage'))
const FormsDataPage = lazy(() => import('../features/formsData/FormsDataPage'))
const RequestsPage = lazy(() => import('../features/requests/RequestsPage'))

const queryClient: QueryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // Identical requests made at the same time are sent once (de-duplication is built into the query cache).
      staleTime: 15_000,
      refetchOnWindowFocus: false,
      // Retry only when the request never got an answer; an answer from the API is final.
      retry: (failureCount, error) => !(error instanceof ApiError) && failureCount < 2,
    },
  },
  queryCache: new QueryCache({
    // Any 401 means the session ended (expired or revoked): go back to the sign-in screen.
    onError: (error) => {
      if (error instanceof ApiError && error.status === 401) {
        queryClient.setQueryData(SESSION_KEY, null)
      }
    },
  }),
})

function RequireAuth() {
  const session = useSession()
  if (session.isPending) {
    return (
      <main className="main">
        <SkeletonRows rows={6} />
      </main>
    )
  }
  return session.data ? <Outlet /> : <Navigate to="/sign-in" replace />
}

/** UI guard only; the API enforces the manager role on every /employees route. */
function RequireManager() {
  const session = useSession()
  return session.data?.role === 'Manager' ? <Outlet /> : <Navigate to="/" replace />
}

export function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <Suspense
          fallback={
            <main className="main">
              <SkeletonRows rows={6} />
            </main>
          }
        >
          <Routes>
            <Route path="/sign-in" element={<SignInPage />} />
            <Route element={<RequireAuth />}>
              <Route element={<AppShell />}>
                <Route index element={<DashboardPage />} />
                <Route path="history" element={<HistoryPage />} />
                <Route path="calendar" element={<CalendarPage />} />
                <Route path="forms-data" element={<FormsDataPage />} />
                <Route path="requests" element={<RequestsPage />} />
                <Route element={<RequireManager />}>
                  <Route path="employees" element={<EmployeesPage />} />
                </Route>
              </Route>
            </Route>
            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
        </Suspense>
      </BrowserRouter>
    </QueryClientProvider>
  )
}

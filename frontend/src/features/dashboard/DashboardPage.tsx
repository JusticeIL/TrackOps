// Container for the dashboard: it owns the data and hands plain values to the cards below.
// The three reads are independent queries, so the browser sends them in parallel.
import { useQuery } from '@tanstack/react-query'
import { useSession } from '../auth/session'
import { api } from '../../shared/api/client'
import type { ActivityItem, CalendarDay } from '../../shared/api/types'
import { ActivityCard, TodaySummaryCard } from './cards'
import { ClockCard } from './ClockCard'
import { useCurrentShift } from './shiftActions'
import './dashboard.css'

const REFRESH_WHILE_OPEN_MS = 60_000

export default function DashboardPage() {
  const session = useSession()
  const currentShift = useCurrentShift()
  const isShiftOpen = Boolean(currentShift.data)
  const today = useQuery({
    queryKey: ['calendar', 'day', 'today'],
    queryFn: () => api<CalendarDay>('GET', '/calendar/days/today'),
    // The server counts an open shift up to its current time, so the totals are refreshed while one is open.
    refetchInterval: isShiftOpen ? REFRESH_WHILE_OPEN_MS : false,
  })
  const activity = useQuery({
    queryKey: ['activity'],
    queryFn: () => api<{ items: ActivityItem[] }>('GET', '/shifts/activity?limit=10'),
  })
  const openBreak = currentShift.data?.breaks.find((pause) => pause.endUtc === null)
  const status = !currentShift.data ? 'מחוץ למשמרת' : openBreak ? 'בהפסקה' : 'במשמרת'

  return (
    <>
      <div className="page-header">
        <h1>
          שלום <span className="latin">{session.data?.displayName}</span>!
        </h1>
      </div>
      <div className="dashboard-grid">
        <ClockCard shift={currentShift.data ?? null} isLoading={currentShift.isPending} />
        <TodaySummaryCard day={today.data} status={status} isLoading={today.isPending} />
        <ActivityCard items={activity.data?.items} isLoading={activity.isPending} />
      </div>
    </>
  )
}

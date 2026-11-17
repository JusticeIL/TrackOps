// Container for "Forms and data". The hours come from the user's real shifts: it reads this month
// and the month before from the calendar API (the same requests the Calendar screen makes, so the
// answers are shared) and derives the numbers in summariseWork. Leave days are sample numbers.
import { useQuery } from '@tanstack/react-query'
import { api, describeError } from '../../shared/api/client'
import type { CalendarDay, CalendarMonth } from '../../shared/api/types'
import { addMonths } from '../../shared/time/zurich'
import { Notice, SkeletonRows } from '../../shared/ui/components'
import { LeaveCard, StatCard, WeekChartCard } from './cards'
import { percentChange, summariseWork } from './workSummary'
import './formsData.css'

/** There is no leave data in the system yet, so these two cards show fixed sample numbers. */
const SAMPLE_LEAVE = {
  vacation: { used: 10, total: 18 },
  sickness: { used: 6, total: 10 },
}

function useMonth(month: string | undefined) {
  return useQuery({
    queryKey: ['calendar', 'month', month],
    queryFn: () => api<CalendarMonth>('GET', `/calendar/months/${month}`),
    enabled: Boolean(month),
  })
}

export default function FormsDataPage() {
  // "Today" is the Zurich date according to the server, not according to this computer.
  const today = useQuery({
    queryKey: ['calendar', 'day', 'today'],
    queryFn: () => api<CalendarDay>('GET', '/calendar/days/today'),
  })
  const month = today.data?.date.slice(0, 7)
  const current = useMonth(month)
  const previous = useMonth(month ? addMonths(month, -1) : undefined)
  const failure = today.error ?? current.error ?? previous.error
  const summary = today.data && current.data && previous.data ? summariseWork(today.data.date, current.data, previous.data) : null

  return (
    <>
      <div className="page-header">
        <h1>טפסים ונתונים</h1>
      </div>
      {failure ? (
        <Notice kind="error">{describeError(failure)}</Notice>
      ) : !summary ? (
        <SkeletonRows rows={6} />
      ) : (
        <div className="data-grid">
          <StatCard
            id="month-title"
            title="סה״כ שעות החודש"
            minutes={summary.monthMinutes}
            change={percentChange(summary.monthMinutes, summary.previousMonthSoFarMinutes)}
            changeLabel="לעומת אותם ימים בחודש הקודם"
            emptyLabel="אין נתונים מהחודש הקודם להשוואה"
            tone="accent"
          />
          <StatCard
            id="average-title"
            title="ממוצע שעות שבועי"
            minutes={summary.weeklyAverageMinutes}
            change={percentChange(summary.lastSevenDaysMinutes, summary.weeklyAverageMinutes)}
            changeLabel="ב-7 הימים האחרונים לעומת הממוצע"
            emptyLabel="לפי 4 השבועות האחרונים"
            tone="dark"
          />
          <LeaveCard id="vacation-title" title="ימי חופשה" unit="ימי חופשה" {...SAMPLE_LEAVE.vacation} />
          <LeaveCard id="sickness-title" title="ימי מחלה" unit="ימי מחלה" {...SAMPLE_LEAVE.sickness} />
          <WeekChartCard week={summary.week} dailyTargetMinutes={summary.dailyTargetMinutes} />
        </div>
      )}
    </>
  )
}

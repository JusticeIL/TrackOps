// Presentational cards of the dashboard. They get data through props and are memoised,
// so a card re-renders only when its own data changes.
import { memo } from 'react'
import type { ActivityItem, ActivityType, CalendarDay } from '../../shared/api/types'
import { formatDate, formatHoursText, formatTime } from '../../shared/time/zurich'
import { SkeletonRows } from '../../shared/ui/components'

interface TodaySummaryProps {
  day: CalendarDay | undefined
  status: string
  isLoading: boolean
}

export const TodaySummaryCard = memo(function TodaySummaryCard({ day, status, isLoading }: TodaySummaryProps) {
  return (
    <section className="card" aria-labelledby="today-title">
      <h2 id="today-title">סיכום שעות של היום</h2>
      {isLoading || !day ? (
        <SkeletonRows rows={3} />
      ) : (
        <dl className="summary-list">
          <div>
            <dt>שעות עבודה</dt>
            <dd>{formatHoursText(day.workedMinutes)}</dd>
          </div>
          <div>
            <dt>זמן הפסקות</dt>
            <dd>{formatHoursText(day.breakMinutes)}</dd>
          </div>
          <div>
            <dt>סטטוס</dt>
            <dd>{status}</dd>
          </div>
        </dl>
      )}
    </section>
  )
})

const ACTIVITY_LABELS: Record<ActivityType, string> = {
  ClockIn: 'כניסה',
  ClockOut: 'יציאה',
  BreakStart: 'תחילת הפסקה',
  BreakEnd: 'סיום הפסקה',
}

export const ActivityCard = memo(function ActivityCard({ items, isLoading }: { items: ActivityItem[] | undefined; isLoading: boolean }) {
  return (
    <section className="card activity-card" aria-labelledby="activity-title">
      <h2 id="activity-title">פעולות אחרונות</h2>
      {isLoading || !items ? (
        <SkeletonRows rows={5} />
      ) : items.length === 0 ? (
        <p className="muted">עדיין אין פעולות.</p>
      ) : (
        <ol className="timeline">
          {items.map((item) => (
            <li key={`${item.type}-${item.atUtc}`}>
              <span className="timeline-label">{ACTIVITY_LABELS[item.type]}</span>
              <span className="muted latin">
                {formatDate(item.atUtc)} · {formatTime(item.atUtc)}
              </span>
            </li>
          ))}
        </ol>
      )}
    </section>
  )
})

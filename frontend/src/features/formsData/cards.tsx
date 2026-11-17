// Presentational cards of the "Forms and data" screen. They take plain values through props.
import type { CSSProperties } from 'react'
import { formatMinutes } from '../../shared/time/zurich'
import type { WeekDay } from './workSummary'

const WEEKDAY_NAMES = ['ראשון', 'שני', 'שלישי', 'רביעי', 'חמישי', 'שישי', 'שבת']
/** The chart is at least this much taller than the daily target, so the target line is always inside it. */
const HEADROOM = 1.25

interface StatCardProps {
  id: string
  title: string
  minutes: number
  /** Change in percent, or null when there is nothing to compare with. */
  change: number | null
  /** Said after the percent, for example "against the same days last month". */
  changeLabel: string
  /** Said instead, when there is nothing to compare with. */
  emptyLabel: string
  tone: 'accent' | 'dark'
}

export function StatCard({ id, title, minutes, change, changeLabel, emptyLabel, tone }: StatCardProps) {
  return (
    <section className={`card stat-card stat-card-${tone}`} aria-labelledby={id}>
      <h2 id={id}>{title}</h2>
      <p className="stat-value latin">{formatMinutes(minutes)}</p>
      <p className="stat-change">
        {change === null ? (
          emptyLabel
        ) : (
          <>
            <span className="latin">
              {change >= 0 ? '+' : '−'}
              {Math.abs(change)}%
            </span>{' '}
            {changeLabel}
          </>
        )}
      </p>
    </section>
  )
}

interface WeekChartProps {
  week: WeekDay[]
  dailyTargetMinutes: number
}

/** Seven bars, Sunday to Saturday. The bars grow from the bottom when the chart appears. */
export function WeekChartCard({ week, dailyTargetMinutes }: WeekChartProps) {
  const scaleMinutes = Math.max(dailyTargetMinutes * HEADROOM, ...week.map((day) => day.workedMinutes))
  const toPercent = (minutes: number): string => `${(minutes / scaleMinutes) * 100}%`

  return (
    <section className="card week-card" aria-labelledby="week-title">
      <h2 id="week-title">שעות עבודה השבוע</h2>
      {/* The bars are decoration; the list below them holds the same numbers as text. */}
      <div className="week-plot" aria-hidden="true">
        <span className="week-target" style={{ insetBlockEnd: toPercent(dailyTargetMinutes) }} />
        {week.map((day, index) => (
          <span key={day.date} className="week-track">
            <span
              className={day.isToday ? 'week-bar week-bar-today' : 'week-bar'}
              style={{ blockSize: toPercent(day.workedMinutes), '--bar-index': index } as CSSProperties}
            />
          </span>
        ))}
      </div>
      <ol className="week-labels">
        {week.map((day, index) => (
          <li key={day.date} className={day.isToday ? 'week-label-today' : undefined}>
            <span>{WEEKDAY_NAMES[index]}</span>
            <span className="latin">{formatMinutes(day.workedMinutes)}</span>
          </li>
        ))}
      </ol>
      <p className="muted week-legend">
        <span className="week-legend-line" aria-hidden="true" /> יעד יומי: <span className="latin">{formatMinutes(dailyTargetMinutes)}</span>
      </p>
    </section>
  )
}

interface LeaveCardProps {
  id: string
  title: string
  unit: string
  used: number
  total: number
}

export function LeaveCard({ id, title, unit, used, total }: LeaveCardProps) {
  return (
    <section className="card leave-card" aria-labelledby={id}>
      <h2 id={id}>{title}</h2>
      <div
        className="leave-track"
        role="progressbar"
        aria-labelledby={id}
        aria-valuemin={0}
        aria-valuemax={total}
        aria-valuenow={used}
        aria-valuetext={`נוצלו ${used} מתוך ${total}`}
      >
        <span className="leave-fill" style={{ inlineSize: `${(used / total) * 100}%` }} />
      </div>
      <p>
        ניצלת <strong className="latin">{used}</strong> {unit} מתוך <strong className="latin">{total}</strong>
      </p>
      <p className="muted">
        נשארו <strong className="latin">{total - used}</strong> עד סוף השנה
      </p>
    </section>
  )
}

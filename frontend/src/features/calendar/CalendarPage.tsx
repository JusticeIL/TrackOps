import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import type { KeyboardEvent } from 'react'
import { api, describeError } from '../../shared/api/client'
import type { CalendarDay, CalendarMonth, DaySummary } from '../../shared/api/types'
import { addMonths, formatDayTitle, formatHoursText, formatMinutes, formatMonthTitle, weekdayOf } from '../../shared/time/zurich'
import { Notice, SkeletonRows } from '../../shared/ui/components'
import { ShiftTable } from '../../shared/ui/ShiftTable'
import { SAMPLE_EVENTS } from './sampleEvents'
import './calendar.css'

const WEEKDAYS = ['א׳', 'ב׳', 'ג׳', 'ד׳', 'ה׳', 'ו׳', 'ש׳']

interface MonthGridProps {
  days: DaySummary[]
  selectedDate: string | null
  today: string | undefined
  onSelect: (date: string) => void
}

function MonthGrid({ days, selectedDate, today, onSelect }: MonthGridProps) {
  const leadingBlanks = weekdayOf(days[0].date)

  return (
    <div className="month-grid">
      {WEEKDAYS.map((weekday) => (
        <span key={weekday} className="month-weekday" aria-hidden="true">
          {weekday}
        </span>
      ))}
      {Array.from({ length: leadingBlanks }, (_, index) => (
        <span key={`blank-${index}`} />
      ))}
      {days.map((day) => {
        const dayNumber = Number(day.date.slice(8))
        const label =
          `${formatDayTitle(day.date)}: ` +
          (day.shiftCount === 0 ? 'אין משמרות' : `עבודה ${formatHoursText(day.workedMinutes)}, שעות נוספות ${formatHoursText(day.overtimeMinutes)}`)
        return (
          <button
            key={day.date}
            type="button"
            className={`day-cell${day.date === today ? ' day-today' : ''}`}
            aria-pressed={day.date === selectedDate}
            aria-label={label}
            onClick={() => onSelect(day.date)}
          >
            <span className="day-number latin">{dayNumber}</span>
            {day.shiftCount > 0 && <span className="day-worked latin">{formatMinutes(day.workedMinutes)}</span>}
            {day.overtimeMinutes > 0 && <span className="badge badge-overtime latin">+{formatMinutes(day.overtimeMinutes)}</span>}
          </button>
        )
      })}
    </div>
  )
}

function DayShifts({ date }: { date: string }) {
  const day = useQuery({
    queryKey: ['calendar', 'day', date],
    queryFn: () => api<CalendarDay>('GET', `/calendar/days/${date}`),
  })

  if (day.isError) {
    return <Notice kind="error">{describeError(day.error)}</Notice>
  }
  if (day.isPending) {
    return <SkeletonRows rows={3} />
  }

  return (
    <div className="stack">
      <p className="row">
        <span>
          עבודה: <strong className="latin">{formatMinutes(day.data.workedMinutes)}</strong>
        </span>
        <span>
          הפסקות: <strong className="latin">{formatMinutes(day.data.breakMinutes)}</strong>
        </span>
        <span>
          שעות נוספות: <strong className="latin">{formatMinutes(day.data.overtimeMinutes)}</strong>
        </span>
      </p>
      <ShiftTable shifts={day.data.shifts} caption={`משמרות ביום ${formatDayTitle(date)}`} />
    </div>
  )
}

/** Events have no data behind them yet, so the tab shows a sample list. It scrolls inside its own frame. */
function DayEvents() {
  return (
    // tabIndex makes the scrolling list reachable by keyboard, so it can be scrolled with the arrow keys.
    <ul className="events-list" tabIndex={0} aria-label="אירועי היום">
      {SAMPLE_EVENTS.map((event) => (
        <li key={event.time} className="event-card">
          <div className="event-text">
            <strong>{event.title}</strong>
            <span className="muted">{event.description}</span>
            <span className="badge badge-open">{event.kind}</span>
          </div>
          <span className="event-time latin">{event.time}</span>
        </li>
      ))}
    </ul>
  )
}

type DayTab = 'shifts' | 'events'

const DAY_TABS: { id: DayTab; label: string }[] = [
  { id: 'shifts', label: 'משמרות' },
  { id: 'events', label: 'אירועים' },
]

/** The selected day, below the calendar: a Shifts tab with the real shifts and an Events tab. */
function DayPanel({ date }: { date: string }) {
  const [tab, setTab] = useState<DayTab>('shifts')

  // Arrow keys move between the two tabs, as keyboard users expect from a tab list.
  function handleKey(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') {
      const next: DayTab = tab === 'shifts' ? 'events' : 'shifts'
      setTab(next)
      document.getElementById(`day-tab-${next}`)?.focus()
    }
  }

  return (
    <section aria-labelledby="day-title">
      <div className="day-tabs-row">
        <div className="day-tabs" role="tablist" aria-label="מה להציג ליום שנבחר" onKeyDown={handleKey}>
          {DAY_TABS.map((item) => (
            <button
              key={item.id}
              id={`day-tab-${item.id}`}
              type="button"
              role="tab"
              className="day-tab"
              aria-selected={tab === item.id}
              aria-controls="day-panel"
              tabIndex={tab === item.id ? 0 : -1}
              onClick={() => setTab(item.id)}
            >
              {item.label}
            </button>
          ))}
        </div>
        <h2 id="day-title" className="day-title">
          {formatDayTitle(date)}
        </h2>
      </div>
      <div id="day-panel" role="tabpanel" aria-labelledby={`day-tab-${tab}`} className="card">
        {tab === 'shifts' ? <DayShifts date={date} /> : <DayEvents />}
      </div>
    </section>
  )
}

export default function CalendarPage() {
  // "Today" is the Zurich date according to the server, not according to this computer.
  const today = useQuery({
    queryKey: ['calendar', 'day', 'today'],
    queryFn: () => api<CalendarDay>('GET', '/calendar/days/today'),
  })
  const [chosenMonth, setChosenMonth] = useState<string | null>(null)
  const [chosenDate, setChosenDate] = useState<string | null>(null)
  const month = chosenMonth ?? today.data?.date.slice(0, 7)
  const selectedDate = chosenDate ?? (chosenMonth === null ? (today.data?.date ?? null) : null)
  const monthData = useQuery({
    queryKey: ['calendar', 'month', month],
    queryFn: () => api<CalendarMonth>('GET', `/calendar/months/${month}`),
    enabled: Boolean(month),
  })

  function moveMonth(delta: number) {
    if (month) {
      setChosenMonth(addMonths(month, delta))
      setChosenDate(null)
    }
  }

  return (
    <>
      <div className="page-header">
        <h1>משמרות ואירועים</h1>
      </div>
      <div className="stack">
        <section className="card" aria-labelledby="month-title">
          <div className="month-header">
            <h2 id="month-title" aria-live="polite">
              {month ? formatMonthTitle(month) : 'טוען…'}
            </h2>
            <span className="row">
              <button type="button" className="button button-small" onClick={() => moveMonth(-1)} disabled={!month}>
                החודש הקודם
              </button>
              <button type="button" className="button button-small" onClick={() => moveMonth(1)} disabled={!month}>
                החודש הבא
              </button>
            </span>
          </div>
          {monthData.isError || today.isError ? (
            <Notice kind="error">{describeError(monthData.error ?? today.error)}</Notice>
          ) : monthData.isPending ? (
            <SkeletonRows rows={6} />
          ) : (
            <>
              <MonthGrid days={monthData.data.days} selectedDate={selectedDate} today={today.data?.date} onSelect={setChosenDate} />
              <p className="muted month-totals">
                סה״כ החודש: <span className="latin">{formatMinutes(monthData.data.totals.workedMinutes)}</span> שעות עבודה ·{' '}
                <span className="latin">{formatMinutes(monthData.data.totals.overtimeMinutes)}</span> שעות נוספות · יעד יומי{' '}
                <span className="latin">{formatMinutes(monthData.data.dailyTargetMinutes)}</span>
              </p>
            </>
          )}
        </section>
        {selectedDate ? <DayPanel date={selectedDate} /> : <p className="muted">בחרו יום כדי לראות את המשמרות והאירועים שלו.</p>}
      </div>
    </>
  )
}

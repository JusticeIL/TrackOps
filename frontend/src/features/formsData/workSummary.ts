// Turns two calendar months (this one and the one before) into the numbers of the "Forms and data" screen.
// A pure function: the same input always gives the same output, and nothing here reads a clock.
import type { CalendarMonth } from '../../shared/api/types'
import { addDays, weekdayOf } from '../../shared/time/zurich'

const DAYS_IN_WEEK = 7
const AVERAGE_WEEKS = 4

export interface WeekDay {
  date: string
  workedMinutes: number
  isToday: boolean
}

interface WorkSummary {
  /** Worked this month so far. */
  monthMinutes: number
  /** Worked in the same days of the previous month: from the 1st up to today's day number. */
  previousMonthSoFarMinutes: number
  /** Average per week over the 28 days before today. */
  weeklyAverageMinutes: number
  /** Worked in the 7 days before today. */
  lastSevenDaysMinutes: number
  /** Sunday to Saturday of the current week. Days that have not come yet hold 0. */
  week: WeekDay[]
  dailyTargetMinutes: number
}

/** `today` is the Zurich date according to the server. */
export function summariseWork(today: string, current: CalendarMonth, previous: CalendarMonth): WorkSummary {
  const workedByDate = new Map<string, number>()
  for (const day of [...previous.days, ...current.days]) {
    workedByDate.set(day.date, day.workedMinutes)
  }
  const workedOn = (date: string): number => workedByDate.get(date) ?? 0
  const workedInDaysBefore = (days: number): number =>
    Array.from({ length: days }, (_, index) => workedOn(addDays(today, -(index + 1)))).reduce((sum, minutes) => sum + minutes, 0)

  const dayOfMonth = Number(today.slice(8))
  const weekStart = addDays(today, -weekdayOf(today))

  return {
    monthMinutes: current.totals.workedMinutes,
    previousMonthSoFarMinutes: previous.days
      .filter((day) => Number(day.date.slice(8)) <= dayOfMonth)
      .reduce((sum, day) => sum + day.workedMinutes, 0),
    weeklyAverageMinutes: Math.round(workedInDaysBefore(DAYS_IN_WEEK * AVERAGE_WEEKS) / AVERAGE_WEEKS),
    lastSevenDaysMinutes: workedInDaysBefore(DAYS_IN_WEEK),
    week: Array.from({ length: DAYS_IN_WEEK }, (_, index) => {
      const date = addDays(weekStart, index)
      return { date, workedMinutes: workedOn(date), isToday: date === today }
    }),
    dailyTargetMinutes: current.dailyTargetMinutes,
  }
}

/** Change from `before` to `now` in whole percent, or null when there is nothing to compare with. */
export function percentChange(now: number, before: number): number | null {
  return before === 0 ? null : Math.round(((now - before) / before) * 100)
}

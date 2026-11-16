// Formatting and date arithmetic for Europe/Zurich. The browser's own time zone is never used:
// every formatter names the zone explicitly, and every instant comes from the server.

export const ZONE = 'Europe/Zurich'
const LOCALE = 'he-IL'

const timeFormat = new Intl.DateTimeFormat(LOCALE, { timeZone: ZONE, hour: '2-digit', minute: '2-digit', hourCycle: 'h23' })
const clockFormat = new Intl.DateTimeFormat(LOCALE, { timeZone: ZONE, hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23' })
const dateFormat = new Intl.DateTimeFormat(LOCALE, { timeZone: ZONE, day: '2-digit', month: '2-digit', year: 'numeric' })
const longDateFormat = new Intl.DateTimeFormat(LOCALE, { timeZone: ZONE, weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' })
const partsFormat = new Intl.DateTimeFormat('en-CA', {
  timeZone: ZONE, year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23',
})

type Instant = string | number

const toDate = (instant: Instant): Date => new Date(instant)

export const formatTime = (instant: Instant): string => timeFormat.format(toDate(instant))
export const formatClock = (instant: Instant): string => clockFormat.format(toDate(instant))
export const formatDate = (instant: Instant): string => dateFormat.format(toDate(instant))
export const formatLongDate = (instant: Instant): string => longDateFormat.format(toDate(instant))
export const formatDateTime = (instant: Instant): string => `${formatDate(instant)} ${formatTime(instant)}`

function zurichParts(instant: Instant): Record<string, string> {
  const parts: Record<string, string> = {}
  for (const part of partsFormat.formatToParts(toDate(instant))) {
    parts[part.type] = part.value
  }
  return parts
}

/** Zurich calendar date of an instant, as yyyy-MM-dd. */
export function zurichDate(instant: Instant): string {
  const parts = zurichParts(instant)
  return `${parts.year}-${parts.month}-${parts.day}`
}

/** Zurich wall time of an instant in the format of a datetime-local input: yyyy-MM-ddTHH:mm. */
export function toZurichInput(instant: Instant): string {
  const parts = zurichParts(instant)
  return `${parts.year}-${parts.month}-${parts.day}T${parts.hour}:${parts.minute}`
}

function zurichOffsetMs(utcMs: number): number {
  const parts = zurichParts(utcMs)
  const asIfUtc = Date.UTC(+parts.year, +parts.month - 1, +parts.day, +parts.hour, +parts.minute, +parts.second)
  return asIfUtc - Math.floor(utcMs / 1000) * 1000
}

/**
 * Converts a Zurich wall time from a datetime-local input to a UTC ISO string.
 * In the hour that repeats when DST ends, the earlier of the two instants is chosen.
 */
export function fromZurichInput(value: string): string {
  const [date, time] = value.split('T')
  const [year, month, day] = date.split('-').map(Number)
  const [hour, minute] = time.split(':').map(Number)
  const wallAsUtc = Date.UTC(year, month - 1, day, hour, minute)
  const firstGuess = wallAsUtc - zurichOffsetMs(wallAsUtc)
  return new Date(wallAsUtc - zurichOffsetMs(firstGuess)).toISOString()
}

export function formatMinutes(minutes: number | null | undefined): string {
  if (minutes === null || minutes === undefined) {
    return '—'
  }
  const hours = Math.floor(minutes / 60)
  return `${hours}:${String(minutes % 60).padStart(2, '0')}`
}

export function formatHoursText(minutes: number): string {
  const hours = Math.floor(minutes / 60)
  const rest = minutes % 60
  if (hours === 0) {
    return `${rest} דקות`
  }
  return rest === 0 ? `${hours} שעות` : `${hours} שעות ו-${rest} דקות`
}

export const minutesBetween = (fromUtc: Instant, toUtc: Instant): number =>
  Math.max(0, Math.floor((toDate(toUtc).getTime() - toDate(fromUtc).getTime()) / 60000))

/** Adds whole months to a yyyy-MM string. */
export function addMonths(month: string, delta: number): string {
  const [year, monthNumber] = month.split('-').map(Number)
  const shifted = new Date(Date.UTC(year, monthNumber - 1 + delta, 1))
  return `${shifted.getUTCFullYear()}-${String(shifted.getUTCMonth() + 1).padStart(2, '0')}`
}

const monthTitleFormat = new Intl.DateTimeFormat(LOCALE, { timeZone: 'UTC', month: 'long', year: 'numeric' })
const dayTitleFormat = new Intl.DateTimeFormat(LOCALE, { timeZone: 'UTC', weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' })

/** Titles for calendar dates (yyyy-MM and yyyy-MM-dd), which are already Zurich local dates. */
export const formatMonthTitle = (month: string): string => monthTitleFormat.format(new Date(`${month}-01T00:00:00Z`))
export const formatDayTitle = (date: string): string => dayTitleFormat.format(new Date(`${date}T00:00:00Z`))
export const weekdayOf = (date: string): number => new Date(`${date}T00:00:00Z`).getUTCDay()

// Shapes of the REST API (docs/api/API.md). All instants are UTC ISO-8601 strings.

type Role = 'Employee' | 'Manager'
export type ShiftStatus = 'Open' | 'Closed' | 'AutoClosed'
export type ActivityType = 'ClockIn' | 'ClockOut' | 'BreakStart' | 'BreakEnd'

export interface User {
  id: number
  username: string
  displayName: string
  role: Role
}

interface Break {
  id: number
  startUtc: string
  endUtc: string | null
  minutes: number | null
}

export interface Shift {
  id: number
  userId: number
  username: string
  displayName: string
  clockInUtc: string
  clockOutUtc: string | null
  status: ShiftStatus
  isCorrected: boolean
  rowVersion: string
  shiftMinutes: number | null
  breakMinutes: number | null
  workedMinutes: number | null
  /** A shift has one break at most; null until it is taken. */
  break: Break | null
}

export interface Paged<T> {
  items: T[]
  page: number
  pageSize: number
  totalItems: number
  totalPages: number
}

export interface ActivityItem {
  type: ActivityType
  atUtc: string
}

export interface ServerTime {
  utc: string
  zone: string
  offsetSeconds: number
}

export interface DaySummary {
  date: string
  workedMinutes: number
  breakMinutes: number
  overtimeMinutes: number
  shiftCount: number
}

export interface CalendarMonth {
  month: string
  dailyTargetMinutes: number
  days: DaySummary[]
  totals: {
    workedMinutes: number
    overtimeMinutes: number
  }
}

export interface CalendarDay {
  date: string
  dailyTargetMinutes: number
  workedMinutes: number
  breakMinutes: number
  overtimeMinutes: number
  shifts: Shift[]
}

export interface AuditItem {
  id: number
  kind: 'ManagerCorrection' | 'AutoClose'
  changedBy: User | null
  changedAtUtc: string
  reason: string
  oldClockInUtc: string
  oldClockOutUtc: string | null
  newClockInUtc: string
  newClockOutUtc: string | null
}

export type RequestType = 'Vacation' | 'Expenses' | 'Training' | 'Sickness' | 'Equipment' | 'ErrorReport' | 'Other'
export type RequestStatus = 'InProgress' | 'Approved' | 'Rejected'

export interface EmployeeRequest {
  id: number
  type: RequestType
  status: RequestStatus
  title: string
  content: string
  sickDays: number | null
  createdAtUtc: string
}

export interface ShiftQuery {
  userId?: number
  from?: string
  to?: string
  status?: ShiftStatus[]
  search?: string
  page?: number
  pageSize?: number
}

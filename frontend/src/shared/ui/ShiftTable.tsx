// One table for shifts, reused by History, the Calendar day list and the manager's Employees screen.
import { memo } from 'react'
import type { ReactNode } from 'react'
import type { Shift } from '../api/types'
import { formatDate, formatMinutes, formatTime } from '../time/zurich'
import { StatusBadge } from './components'

interface ShiftTableProps {
  shifts: Shift[]
  caption: string
  showEmployee?: boolean
  renderActions?: (shift: Shift) => ReactNode
}

export const ShiftTable = memo(function ShiftTable({ shifts, caption, showEmployee = false, renderActions }: ShiftTableProps) {
  if (shifts.length === 0) {
    return <p className="muted">לא נמצאו משמרות.</p>
  }

  return (
    <div className="table-wrap">
      <table className="table">
        <caption className="visually-hidden">{caption}</caption>
        <thead>
          <tr>
            {showEmployee && <th scope="col">עובד</th>}
            <th scope="col">תאריך</th>
            <th scope="col">כניסה</th>
            <th scope="col">יציאה</th>
            <th scope="col">הפסקה</th>
            <th scope="col">שעות עבודה</th>
            <th scope="col">סטטוס</th>
            {renderActions && <th scope="col">פעולות</th>}
          </tr>
        </thead>
        <tbody>
          {shifts.map((shift) => (
            <tr key={shift.id}>
              {showEmployee && <td>{shift.displayName}</td>}
              <td className="latin">{formatDate(shift.clockInUtc)}</td>
              <td className="latin">{formatTime(shift.clockInUtc)}</td>
              <td className="latin">{shift.clockOutUtc ? formatTime(shift.clockOutUtc) : '—'}</td>
              <td className="latin">
                {shift.break === null ? '—' : formatMinutes(shift.breakMinutes)}
              </td>
              <td className="latin">{formatMinutes(shift.workedMinutes)}</td>
              <td>
                <StatusBadge status={shift.status} />
                {shift.isCorrected && <span className="badge badge-closed"> תוקנה</span>}
              </td>
              {renderActions && <td>{renderActions(shift)}</td>}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
})

// Manager screen: every employee's shifts, with an employee autocomplete and corrections.
// The route is hidden from employees in the UI, and the API refuses them with 403 regardless.
import { useCallback, useState } from 'react'
import { describeError } from '../../shared/api/client'
import type { Shift, User } from '../../shared/api/types'
import { Notice, Pagination, SkeletonRows } from '../../shared/ui/components'
import { EMPTY_FILTERS, ShiftFilters, useShiftSearch } from '../../shared/ui/ShiftFilters'
import type { ShiftFilterValues } from '../../shared/ui/ShiftFilters'
import { ShiftTable } from '../../shared/ui/ShiftTable'
import { CorrectShiftDialog } from './CorrectShiftDialog'
import { EmployeePicker } from './EmployeePicker'
import './employees.css'

export default function EmployeesPage() {
  const [filters, setFilters] = useState<ShiftFilterValues>(EMPTY_FILTERS)
  const [employee, setEmployee] = useState<User | null>(null)
  const [page, setPage] = useState(1)
  const [shiftToCorrect, setShiftToCorrect] = useState<Shift | null>(null)
  const result = useShiftSearch('employee-shifts', '/employees/shifts', filters, page, employee?.id)

  function changeFilters(next: ShiftFilterValues) {
    setFilters(next)
    setPage(1)
  }

  function changeEmployee(next: User | null) {
    setEmployee(next)
    setPage(1)
  }

  // A stable function, so the memoised table does not re-render when only the dialog opens.
  const renderActions = useCallback(
    (shift: Shift) => (
      <button type="button" className="button button-small" onClick={() => setShiftToCorrect(shift)}>
        תיקון
      </button>
    ),
    [],
  )

  return (
    <>
      <div className="page-header">
        <h1>עובדים</h1>
      </div>
      <section className="card" aria-label="משמרות כל העובדים">
        <ShiftFilters idPrefix="employees" values={filters} searchLabel="חיפוש לפי שם עובד" onChange={changeFilters}>
          <EmployeePicker selected={employee} onSelect={changeEmployee} />
        </ShiftFilters>
        {result.isError ? (
          <Notice kind="error">{describeError(result.error)}</Notice>
        ) : result.isPending ? (
          <SkeletonRows rows={8} />
        ) : (
          <>
            <ShiftTable shifts={result.data.items} caption="משמרות כל העובדים" showEmployee renderActions={renderActions} />
            <Pagination page={result.data.page} totalPages={result.data.totalPages} totalItems={result.data.totalItems} onPageChange={setPage} />
          </>
        )}
      </section>
      {shiftToCorrect && <CorrectShiftDialog key={shiftToCorrect.id} shift={shiftToCorrect} onClose={() => setShiftToCorrect(null)} />}
    </>
  )
}

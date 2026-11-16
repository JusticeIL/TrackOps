import { useState } from 'react'
import { describeError } from '../../shared/api/client'
import { Notice, Pagination, SkeletonRows } from '../../shared/ui/components'
import { EMPTY_FILTERS, ShiftFilters, useShiftSearch } from '../../shared/ui/ShiftFilters'
import type { ShiftFilterValues } from '../../shared/ui/ShiftFilters'
import { ShiftTable } from '../../shared/ui/ShiftTable'

export default function HistoryPage() {
  const [filters, setFilters] = useState<ShiftFilterValues>(EMPTY_FILTERS)
  const [page, setPage] = useState(1)
  const result = useShiftSearch('history', '/shifts', filters, page)

  function changeFilters(next: ShiftFilterValues) {
    setFilters(next)
    setPage(1)
  }

  return (
    <>
      <div className="page-header">
        <h1>היסטוריית משמרות</h1>
      </div>
      <section className="card" aria-label="המשמרות שלי">
        <ShiftFilters idPrefix="history" values={filters} searchLabel="חיפוש בהערות" onChange={changeFilters} />
        {result.isError ? (
          <Notice kind="error">{describeError(result.error)}</Notice>
        ) : result.isPending ? (
          <SkeletonRows rows={8} />
        ) : (
          <>
            <ShiftTable shifts={result.data.items} caption="המשמרות שלי" />
            <Pagination page={result.data.page} totalPages={result.data.totalPages} totalItems={result.data.totalItems} onPageChange={setPage} />
          </>
        )}
      </section>
    </>
  )
}

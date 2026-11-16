// Filter bar and paged search shared by "my history" and the manager's "all shifts".
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { api } from '../api/client'
import type { Paged, Shift, ShiftQuery, ShiftStatus } from '../api/types'
import { useDebouncedValue } from './components'

export interface ShiftFilterValues {
  from: string
  to: string
  statuses: ShiftStatus[]
  search: string
}

export const EMPTY_FILTERS: ShiftFilterValues = { from: '', to: '', statuses: [], search: '' }

const STATUS_OPTIONS: { value: ShiftStatus; label: string }[] = [
  { value: 'Open', label: 'פתוחה' },
  { value: 'Closed', label: 'סגורה' },
  { value: 'AutoClosed', label: 'נסגרה אוטומטית' },
]
const SEARCH_DEBOUNCE_MS = 300
const PAGE_SIZE = 10

interface ShiftFiltersProps {
  idPrefix: string
  values: ShiftFilterValues
  searchLabel: string
  onChange: (values: ShiftFilterValues) => void
  children?: ReactNode
}

export function ShiftFilters({ idPrefix, values, searchLabel, onChange, children }: ShiftFiltersProps) {
  function toggleStatus(status: ShiftStatus) {
    const statuses = values.statuses.includes(status) ? values.statuses.filter((item) => item !== status) : [...values.statuses, status]
    onChange({ ...values, statuses })
  }

  return (
    <form className="filters" role="search" onSubmit={(event) => event.preventDefault()}>
      {children}
      <div className="field">
        <label htmlFor={`${idPrefix}-from`}>מתאריך</label>
        <input id={`${idPrefix}-from`} type="date" className="input" value={values.from} onChange={(event) => onChange({ ...values, from: event.target.value })} />
      </div>
      <div className="field">
        <label htmlFor={`${idPrefix}-to`}>עד תאריך</label>
        <input id={`${idPrefix}-to`} type="date" className="input" value={values.to} onChange={(event) => onChange({ ...values, to: event.target.value })} />
      </div>
      <div className="field">
        <label htmlFor={`${idPrefix}-search`}>{searchLabel}</label>
        <input
          id={`${idPrefix}-search`}
          type="search"
          className="input"
          maxLength={100}
          value={values.search}
          onChange={(event) => onChange({ ...values, search: event.target.value })}
        />
      </div>
      <fieldset className="field checks">
        <legend className="visually-hidden">סטטוס</legend>
        {STATUS_OPTIONS.map((option) => (
          <label key={option.value} className="row">
            <input type="checkbox" checked={values.statuses.includes(option.value)} onChange={() => toggleStatus(option.value)} />
            {option.label}
          </label>
        ))}
      </fieldset>
      <button type="button" className="button button-small" onClick={() => onChange(EMPTY_FILTERS)}>
        ניקוי
      </button>
    </form>
  )
}

/**
 * Runs a paged shift search with the HTTP QUERY method.
 * The search text is debounced, and the previous page stays on screen while the next one loads.
 */
export function useShiftSearch(keyRoot: string, path: string, filters: ShiftFilterValues, page: number, userId?: number) {
  const search = useDebouncedValue(filters.search.trim(), SEARCH_DEBOUNCE_MS)
  const body: ShiftQuery = {
    userId,
    from: filters.from || undefined,
    to: filters.to || undefined,
    status: filters.statuses.length > 0 ? filters.statuses : undefined,
    search: search || undefined,
    page,
    pageSize: PAGE_SIZE,
  }

  return useQuery({
    queryKey: [keyRoot, body],
    queryFn: ({ signal }) => api<Paged<Shift>>('QUERY', path, { body, signal }),
    placeholderData: keepPreviousData,
  })
}

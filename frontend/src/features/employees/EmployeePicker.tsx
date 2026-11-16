// Autocomplete for choosing one employee. It follows the ARIA combobox pattern, so it works with
// the keyboard (arrows, Enter, Escape) and with screen readers. The search is debounced: one
// request after the user pauses, not one per keystroke.
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import type { KeyboardEvent } from 'react'
import { api } from '../../shared/api/client'
import type { User } from '../../shared/api/types'
import { useDebouncedValue } from '../../shared/ui/components'

const DEBOUNCE_MS = 250
const MAX_SUGGESTIONS = 8

interface EmployeePickerProps {
  selected: User | null
  onSelect: (user: User | null) => void
}

export function EmployeePicker({ selected, onSelect }: EmployeePickerProps) {
  const [text, setText] = useState('')
  const [isOpen, setIsOpen] = useState(false)
  const [activeIndex, setActiveIndex] = useState(-1)
  const search = useDebouncedValue(text.trim(), DEBOUNCE_MS)
  const suggestions = useQuery({
    queryKey: ['employees', search],
    queryFn: ({ signal }) => api<{ items: User[] }>('QUERY', '/employees', { body: { search, limit: MAX_SUGGESTIONS }, signal }),
    enabled: isOpen && search.length > 0,
    placeholderData: keepPreviousData,
  })
  const options = search.length > 0 ? (suggestions.data?.items ?? []) : []

  function choose(user: User) {
    onSelect(user)
    setText('')
    setIsOpen(false)
    setActiveIndex(-1)
  }

  function handleKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === 'ArrowDown') {
      event.preventDefault()
      setIsOpen(true)
      setActiveIndex((index) => Math.min(index + 1, options.length - 1))
    } else if (event.key === 'ArrowUp') {
      event.preventDefault()
      setActiveIndex((index) => Math.max(index - 1, 0))
    } else if (event.key === 'Enter' && activeIndex >= 0 && options[activeIndex]) {
      event.preventDefault()
      choose(options[activeIndex])
    } else if (event.key === 'Escape') {
      setIsOpen(false)
    }
  }

  return (
    <div className="field employee-picker">
      <label htmlFor="employee-search">עובד</label>
      {selected ? (
        <span className="row">
          <span className="badge badge-open latin">{selected.displayName}</span>
          <button type="button" className="button button-small" onClick={() => onSelect(null)}>
            הסרת הסינון לפי עובד
          </button>
        </span>
      ) : (
        <>
          <input
            id="employee-search"
            className="input latin"
            role="combobox"
            aria-expanded={isOpen && options.length > 0}
            aria-controls="employee-options"
            aria-autocomplete="list"
            aria-activedescendant={activeIndex >= 0 ? `employee-option-${activeIndex}` : undefined}
            autoComplete="off"
            placeholder="הקלידו שם…"
            value={text}
            onChange={(event) => {
              setText(event.target.value)
              setIsOpen(true)
              setActiveIndex(-1)
            }}
            onKeyDown={handleKeyDown}
            onBlur={() => setIsOpen(false)}
          />
          {isOpen && search.length > 0 && (
            <ul id="employee-options" className="card employee-options" role="listbox" aria-label="עובדים מתאימים">
              {options.length === 0 && !suggestions.isFetching && <li className="muted">לא נמצאו עובדים.</li>}
              {options.map((user, index) => (
                <li
                  key={user.id}
                  id={`employee-option-${index}`}
                  role="option"
                  aria-selected={index === activeIndex}
                  className="employee-option latin"
                  // mousedown fires before the input's blur, so the click is not lost when the list closes.
                  onMouseDown={(event) => {
                    event.preventDefault()
                    choose(user)
                  }}
                >
                  {user.displayName}
                </li>
              ))}
            </ul>
          )}
        </>
      )}
    </div>
  )
}

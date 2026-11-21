// The two forms of the Requests screen: a new request, and a sickness report. Both send
// POST /requests; the server sets the status and takes the filing time from the external time API.
// The server is the judge of what is valid: the checks here only spare a round trip, and when the
// server refuses a field, its error is shown under that field.
// The dialog is the native <dialog> element: it traps focus and closes on Escape by itself.
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { api, ApiError, describeError } from '../../shared/api/client'
import type { EmployeeRequest, RequestType } from '../../shared/api/types'
import { Notice } from '../../shared/ui/components'
import { REQUEST_FORM_TYPES, TYPE_LABELS } from './requestTexts'

export const REQUESTS_KEY = ['requests'] as const

const MIN_TEXT_LENGTH = 3
const MAX_TITLE_LENGTH = 100
const MAX_CONTENT_LENGTH = 2000
const MAX_SICK_DAYS = 30

export type RequestFormKind = 'request' | 'sickness'

type Field = 'title' | 'type' | 'content' | 'sickDays'

/** What to tell the user when the server refuses a field. */
const FIELD_MESSAGES: Record<Field, string> = {
  title: `הכותרת צריכה להיות שורה אחת של ${MIN_TEXT_LENGTH}–${MAX_TITLE_LENGTH} תווים, בלי תווים נסתרים.`,
  type: 'יש לבחור נושא מהרשימה.',
  content: `הפירוט יכול להכיל עד ${MAX_CONTENT_LENGTH} תווים, בלי תווים נסתרים.`,
  sickDays: `מספר ימי המחלה צריך להיות בין 1 ל-${MAX_SICK_DAYS}.`,
}

/** The fields the server named, read from keys such as "title" or "$.title". */
function refusedFields(error: unknown): Set<Field> {
  const fields = new Set<Field>()
  if (error instanceof ApiError) {
    for (const key of Object.keys(error.fieldErrors)) {
      const name = key.replace(/^\$\.?/, '')
      if (name in FIELD_MESSAGES) {
        fields.add(name as Field)
      }
    }
  }
  return fields
}

/** The title a sickness report gets when the user leaves the title empty. */
const defaultSicknessTitle = (days: number): string => (days === 1 ? 'דיווח מחלה - יום אחד' : `דיווח מחלה - ${days} ימים`)

function FieldError({ field, refused }: { field: Field; refused: Set<Field> }) {
  return refused.has(field) ? (
    <span id={`request-${field}-error`} className="field-error">
      {FIELD_MESSAGES[field]}
    </span>
  ) : null
}

export function RequestDialog({ kind, onClose }: { kind: RequestFormKind; onClose: () => void }) {
  const isSickness = kind === 'sickness'
  const dialogRef = useRef<HTMLDialogElement>(null)
  const queryClient = useQueryClient()
  const [title, setTitle] = useState('')
  const [type, setType] = useState<RequestType | ''>('')
  const [sickDays, setSickDays] = useState('1')
  const [content, setContent] = useState('')
  const days = Number(sickDays)
  const filing = useMutation({
    mutationFn: () =>
      api<EmployeeRequest>('POST', '/requests', {
        body: isSickness
          ? { type: 'Sickness', title: title.trim() || defaultSicknessTitle(days), content: content.trim(), sickDays: days }
          : { type, title: title.trim(), content: content.trim() },
      }),
    onSuccess: (created) => {
      // The new request goes to the top of the list at once; no second request is needed.
      queryClient.setQueryData<{ items: EmployeeRequest[] }>(REQUESTS_KEY, (old) => ({ items: [created, ...(old?.items ?? [])] }))
      onClose()
    },
  })

  useEffect(() => {
    dialogRef.current?.showModal()
  }, [])

  const refused = refusedFields(filing.error)
  const isTitleValid = isSickness ? title.trim().length === 0 || title.trim().length >= MIN_TEXT_LENGTH : title.trim().length >= MIN_TEXT_LENGTH
  const isValid = isSickness
    ? isTitleValid && Number.isInteger(days) && days >= 1 && days <= MAX_SICK_DAYS
    : isTitleValid && type !== '' && content.trim().length >= MIN_TEXT_LENGTH

  // Accessibility attributes for a field the server refused.
  function invalidProps(field: Field) {
    return refused.has(field) ? { 'aria-invalid': true, 'aria-describedby': `request-${field}-error` } : {}
  }

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (isValid) {
      filing.mutate()
    }
  }

  return (
    <dialog ref={dialogRef} className="dialog" aria-labelledby="request-form-title" onClose={onClose}>
      <form className="stack" onSubmit={handleSubmit}>
        <h2 id="request-form-title">{isSickness ? 'דיווח מחלה' : 'בקשה חדשה'}</h2>
        <p className="muted">מלאו את הפרטים ושלחו. הבקשה תירשם ברשימה במצב ״בטיפול״.</p>

        <div className="field">
          <label htmlFor="request-title">כותרת הבקשה{isSickness ? ' (לא חובה)' : ''}</label>
          <input
            id="request-title"
            className="input"
            dir="auto"
            minLength={MIN_TEXT_LENGTH}
            maxLength={MAX_TITLE_LENGTH}
            value={title}
            onChange={(event) => setTitle(event.target.value)}
            required={!isSickness}
            {...invalidProps('title')}
          />
          <FieldError field="title" refused={refused} />
        </div>

        {isSickness ? (
          <div className="field">
            <label htmlFor="request-sick-days">ימי מחלה</label>
            <input
              id="request-sick-days"
              type="number"
              className="input latin"
              min={1}
              max={MAX_SICK_DAYS}
              step={1}
              value={sickDays}
              onChange={(event) => setSickDays(event.target.value)}
              required
              {...invalidProps('sickDays')}
            />
            <FieldError field="sickDays" refused={refused} />
          </div>
        ) : (
          <div className="field">
            <label htmlFor="request-type">נושא הבקשה</label>
            <select
              id="request-type"
              className="input"
              value={type}
              onChange={(event) => setType(event.target.value as RequestType | '')}
              required
              {...invalidProps('type')}
            >
              <option value="">בחרו נושא</option>
              {REQUEST_FORM_TYPES.map((option) => (
                <option key={option} value={option}>
                  {TYPE_LABELS[option]}
                </option>
              ))}
            </select>
            <FieldError field="type" refused={refused} />
          </div>
        )}

        <div className="field">
          <label htmlFor="request-content">{isSickness ? 'הסבר נוסף (לא חובה)' : 'תוכן הבקשה'}</label>
          <textarea
            id="request-content"
            className="input"
            dir="auto"
            rows={5}
            minLength={isSickness ? undefined : MIN_TEXT_LENGTH}
            maxLength={MAX_CONTENT_LENGTH}
            value={content}
            onChange={(event) => setContent(event.target.value)}
            required={!isSickness}
            {...invalidProps('content')}
          />
          <FieldError field="content" refused={refused} />
        </div>

        {/* A general message only when the server's refusal is not about one of the fields above. */}
        {filing.isError && refused.size === 0 && <Notice>{describeError(filing.error)}</Notice>}

        <div className="row">
          <button type="submit" className="button button-primary" disabled={filing.isPending || !isValid}>
            {filing.isPending ? 'שולח…' : isSickness ? 'שליחת הדיווח' : 'שליחת הבקשה'}
          </button>
          <button type="button" className="button" onClick={() => dialogRef.current?.close()}>
            ביטול
          </button>
        </div>
      </form>
    </dialog>
  )
}

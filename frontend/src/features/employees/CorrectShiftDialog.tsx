// Manager correction of one shift. Times are typed as Zurich wall time and sent as UTC instants.
// The dialog is the native <dialog> element: it traps focus and closes on Escape by itself.
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { api, ApiError, describeError } from '../../shared/api/client'
import type { AuditItem, Shift } from '../../shared/api/types'
import { formatDateTime, fromZurichInput, toZurichInput } from '../../shared/time/zurich'
import { Notice, SkeletonRows } from '../../shared/ui/components'

const MIN_REASON_LENGTH = 3
const MAX_REASON_LENGTH = 300

const KIND_LABELS: Record<AuditItem['kind'], string> = { ManagerCorrection: 'תיקון מנהל', AutoClose: 'סגירה אוטומטית' }

function AuditTrail({ shift }: { shift: Shift }) {
  const audit = useQuery({
    queryKey: ['audit', shift.id],
    queryFn: () => api<{ items: AuditItem[] }>('GET', `/employees/${shift.userId}/shifts/${shift.id}/audit`),
  })

  if (audit.isPending) {
    return <SkeletonRows rows={2} />
  }
  if (audit.isError || audit.data.items.length === 0) {
    return <p className="muted">אין שינויים קודמים במשמרת זו.</p>
  }
  return (
    <ul className="audit-list">
      {audit.data.items.map((item) => (
        <li key={item.id}>
          <strong>{KIND_LABELS[item.kind]}</strong>
          {item.changedBy && <span className="latin"> · {item.changedBy.displayName}</span>}
          <span className="muted latin"> · {formatDateTime(item.changedAtUtc)}</span>
          <br />
          <span className="latin">
            {formatDateTime(item.oldClockInUtc)} – {item.oldClockOutUtc ? formatDateTime(item.oldClockOutUtc) : '…'} ←{' '}
            {formatDateTime(item.newClockInUtc)} – {item.newClockOutUtc ? formatDateTime(item.newClockOutUtc) : '…'}
          </span>
          <br />
          <span>{item.reason}</span>
        </li>
      ))}
    </ul>
  )
}

export function CorrectShiftDialog({ shift, onClose }: { shift: Shift; onClose: () => void }) {
  const dialogRef = useRef<HTMLDialogElement>(null)
  const queryClient = useQueryClient()
  const [clockIn, setClockIn] = useState(toZurichInput(shift.clockInUtc))
  const [clockOut, setClockOut] = useState(shift.clockOutUtc ? toZurichInput(shift.clockOutUtc) : '')
  const [reason, setReason] = useState('')
  const correction = useMutation({
    mutationFn: () =>
      api<Shift>('PATCH', `/employees/${shift.userId}/shifts/${shift.id}`, {
        body: {
          clockInUtc: fromZurichInput(clockIn),
          clockOutUtc: clockOut ? fromZurichInput(clockOut) : undefined,
          reason: reason.trim(),
          rowVersion: shift.rowVersion,
        },
      }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['employee-shifts'] })
      void queryClient.invalidateQueries({ queryKey: ['audit', shift.id] })
      onClose()
    },
  })

  useEffect(() => {
    dialogRef.current?.showModal()
  }, [])

  const fieldErrors = correction.error instanceof ApiError ? Object.keys(correction.error.fieldErrors) : []
  const isReasonValid = reason.trim().length >= MIN_REASON_LENGTH

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (isReasonValid) {
      correction.mutate()
    }
  }

  return (
    <dialog ref={dialogRef} className="dialog" aria-labelledby="correct-title" onClose={onClose}>
      <form className="stack" onSubmit={handleSubmit}>
        <h2 id="correct-title">
          תיקון משמרת של <span className="latin">{shift.displayName}</span>
        </h2>
        <p className="muted">הזמנים הם לפי שעון ציריך. כל תיקון נשמר ביומן השינויים יחד עם הסיבה.</p>
        <div className="field">
          <label htmlFor="correct-in">כניסה</label>
          <input id="correct-in" type="datetime-local" className="input latin" value={clockIn} onChange={(event) => setClockIn(event.target.value)} required />
        </div>
        <div className="field">
          <label htmlFor="correct-out">יציאה {shift.clockOutUtc === null && '(ריק = המשמרת נשארת פתוחה)'}</label>
          <input
            id="correct-out"
            type="datetime-local"
            className="input latin"
            value={clockOut}
            onChange={(event) => setClockOut(event.target.value)}
            required={shift.clockOutUtc !== null}
          />
        </div>
        <div className="field">
          <label htmlFor="correct-reason">סיבת התיקון</label>
          <textarea
            id="correct-reason"
            className="input"
            rows={2}
            minLength={MIN_REASON_LENGTH}
            maxLength={MAX_REASON_LENGTH}
            value={reason}
            onChange={(event) => setReason(event.target.value)}
            required
          />
        </div>
        {correction.isError && (
          <Notice kind="error">
            {describeError(correction.error)}
            {fieldErrors.length > 0 && ` (${fieldErrors.join(', ')})`}
          </Notice>
        )}
        <div className="row">
          <button type="submit" className="button button-primary" disabled={correction.isPending || !isReasonValid}>
            שמירת התיקון
          </button>
          <button type="button" className="button" onClick={() => dialogRef.current?.close()}>
            ביטול
          </button>
        </div>
        <h3>יומן שינויים</h3>
        <AuditTrail shift={shift} />
      </form>
    </dialog>
  )
}

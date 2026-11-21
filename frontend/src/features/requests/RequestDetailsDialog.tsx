// One request in full: what was filed, when, and its status. Everything the user typed is shown
// as plain text (React escapes it) with dir="auto", so Hebrew, English or a mix reads in its own
// direction, and the content keeps its line breaks.
import { useEffect, useRef } from 'react'
import type { EmployeeRequest } from '../../shared/api/types'
import { formatDateTime } from '../../shared/time/zurich'
import { describeType, formatRequestNumber, STATUS_CLASSES, STATUS_LABELS } from './requestTexts'

export function RequestDetailsDialog({ request, onClose }: { request: EmployeeRequest; onClose: () => void }) {
  const dialogRef = useRef<HTMLDialogElement>(null)

  useEffect(() => {
    dialogRef.current?.showModal()
  }, [])

  return (
    <dialog ref={dialogRef} className="dialog" aria-labelledby="request-details-title" onClose={onClose}>
      <div className="stack">
        <h2 id="request-details-title">
          בקשה <span className="latin">{formatRequestNumber(request.id)}</span>
        </h2>
        <dl className="details-list">
          <div>
            <dt>סוג</dt>
            <dd>{describeType(request)}</dd>
          </div>
          <div>
            <dt>סטטוס</dt>
            <dd>
              <span className={`badge ${STATUS_CLASSES[request.status]}`}>{STATUS_LABELS[request.status]}</span>
            </dd>
          </div>
          <div>
            <dt>הוגשה</dt>
            <dd>
              <span className="latin">{formatDateTime(request.createdAtUtc)}</span>
            </dd>
          </div>
          <div>
            <dt>כותרת</dt>
            <dd dir="auto">{request.title}</dd>
          </div>
        </dl>
        <section className="stack request-content-section" aria-labelledby="request-details-content">
          <h3 id="request-details-content">פירוט</h3>
          {request.content ? (
            <p className="request-content" dir="auto">
              {request.content}
            </p>
          ) : (
            <p className="muted">אין פירוט נוסף.</p>
          )}
        </section>
        <div className="row">
          <button type="button" className="button" onClick={() => dialogRef.current?.close()}>
            סגירה
          </button>
        </div>
      </div>
    </dialog>
  )
}

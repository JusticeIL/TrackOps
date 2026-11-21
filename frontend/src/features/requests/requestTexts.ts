// Hebrew names of request types and statuses, and the sample history of the Requests screen.
import type { EmployeeRequest, RequestStatus, RequestType } from '../../shared/api/types'

export const TYPE_LABELS: Record<RequestType, string> = {
  Vacation: 'בקשת חופשה',
  Expenses: 'החזר הוצאות',
  Training: 'בקשת הכשרה',
  Sickness: 'דיווח מחלה',
  Equipment: 'בקשת ציוד',
  ErrorReport: 'דיווח למנהל על טעות',
  Other: 'אחר',
}

/** Sickness has its own form, so the "new request" form offers every type but that one. */
export const REQUEST_FORM_TYPES: RequestType[] = ['Vacation', 'Expenses', 'Training', 'Equipment', 'ErrorReport', 'Other']

export const STATUS_LABELS: Record<RequestStatus, string> = { InProgress: 'בטיפול', Approved: 'אושר', Rejected: 'נדחה' }
export const STATUS_CLASSES: Record<RequestStatus, string> = {
  InProgress: 'badge-progress',
  Approved: 'badge-approved',
  Rejected: 'badge-rejected',
}

/**
 * Sample history, shown below the user's own requests so the screen is not empty on a new
 * installation. These five rows exist only here. A request filed through one of the forms is
 * stored by the API and gets a number from 1001 up, so it never collides with a sample.
 */
export const SAMPLE_REQUESTS: EmployeeRequest[] = [
  { id: 5, type: 'Vacation', status: 'Approved', title: 'חופשה שנתית - 5 ימים', content: '', sickDays: null, createdAtUtc: '2024-01-15T08:00:00Z' },
  { id: 4, type: 'Expenses', status: 'InProgress', title: 'החזר נסיעות עבודה', content: '', sickDays: null, createdAtUtc: '2024-01-12T08:00:00Z' },
  { id: 3, type: 'Training', status: 'Rejected', title: 'קורס פיתוח מקצועי', content: '', sickDays: null, createdAtUtc: '2024-01-08T08:00:00Z' },
  { id: 2, type: 'Sickness', status: 'Approved', title: 'היעדרות רפואית - יום אחד', content: '', sickDays: 1, createdAtUtc: '2024-01-05T08:00:00Z' },
  { id: 1, type: 'Equipment', status: 'InProgress', title: 'מחשב נייד חדש', content: '', sickDays: null, createdAtUtc: '2024-01-03T08:00:00Z' },
]

const NUMBER_DIGITS = 4

/** "#0005", "#1001": the number people see is the request's id. */
export const formatRequestNumber = (id: number): string => `#${String(id).padStart(NUMBER_DIGITS, '0')}`

/** The type in Hebrew; a sickness report also says how many days it covers. */
export function describeType(request: EmployeeRequest): string {
  if (request.type !== 'Sickness' || request.sickDays === null) {
    return TYPE_LABELS[request.type]
  }
  const days = request.sickDays === 1 ? 'יום אחד' : `${request.sickDays} ימים`
  return `${TYPE_LABELS.Sickness} · ${days}`
}

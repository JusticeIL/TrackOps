// Presentational building blocks: they receive props and render, nothing else.
import { useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import type { ShiftStatus } from '../api/types'

export function Logo({ size = 36 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 120 120" role="img" aria-label="TrackOps">
      <rect width="120" height="120" rx="26" fill="#0A0A0A" />
      <rect x="30" y="34" width="48" height="14" rx="7" fill="#FAFAFA" />
      <rect x="53" y="34" width="14" height="56" rx="7" fill="#FAFAFA" />
      <circle cx="90" cy="41" r="7" fill="#5AC2C5" />
    </svg>
  )
}

export function Skeleton({ width = '100%', height = 20 }: { width?: number | string; height?: number }) {
  return <span className="skeleton" style={{ inlineSize: width, blockSize: height }} aria-hidden="true" />
}

/** Placeholder rows shown while a list loads, so the page does not jump when data arrives. */
export function SkeletonRows({ rows = 5 }: { rows?: number }) {
  return (
    <div className="stack" role="status">
      <span className="visually-hidden">טוען…</span>
      {Array.from({ length: rows }, (_, index) => (
        <Skeleton key={index} height={28} />
      ))}
    </div>
  )
}

const STATUS_LABELS: Record<ShiftStatus, string> = { Open: 'פתוחה', Closed: 'סגורה', AutoClosed: 'נסגרה אוטומטית' }
const STATUS_CLASSES: Record<ShiftStatus, string> = { Open: 'badge-open', Closed: 'badge-closed', AutoClosed: 'badge-auto' }

export function StatusBadge({ status }: { status: ShiftStatus }) {
  return <span className={`badge ${STATUS_CLASSES[status]}`}>{STATUS_LABELS[status]}</span>
}

export function Notice({ kind, children }: { kind: 'error' | 'info'; children: ReactNode }) {
  return (
    <p className={`notice notice-${kind}`} role={kind === 'error' ? 'alert' : 'status'}>
      {children}
    </p>
  )
}

interface PreviewImageProps {
  src: string
  /** Says in words what the picture shows; it is the picture's content for screen readers. */
  alt: string
  /** Size in CSS pixels. The files are twice as large, so they stay sharp on dense screens. */
  width: number
  height: number
  /** Without the card frame, for a picture that is not a card (a row of buttons). */
  plain?: boolean
}

/** A static picture of a planned screen, embedded where the real screen would be. */
export function PreviewImage({ src, alt, width, height, plain = false }: PreviewImageProps) {
  return (
    <img
      className={plain ? 'preview-image preview-image-plain' : 'preview-image'}
      src={src}
      alt={alt}
      width={width}
      height={height}
      loading="lazy"
      decoding="async"
    />
  )
}

export function PreviewNote() {
  return <p className="muted preview-note">תצוגה לדוגמה בלבד: המסך מוצג כתמונה, והנתונים שבה אינם אמיתיים.</p>
}

interface PaginationProps {
  page: number
  totalPages: number
  totalItems: number
  onPageChange: (page: number) => void
}

export function Pagination({ page, totalPages, totalItems, onPageChange }: PaginationProps) {
  return (
    <nav className="pagination" aria-label="דפדוף בין עמודים">
      <span className="muted">
        עמוד {page} מתוך {Math.max(totalPages, 1)} · {totalItems} רשומות
      </span>
      <span className="row">
        <button type="button" className="button button-small" disabled={page <= 1} onClick={() => onPageChange(page - 1)}>
          הקודם
        </button>
        <button type="button" className="button button-small" disabled={page >= totalPages} onClick={() => onPageChange(page + 1)}>
          הבא
        </button>
      </span>
    </nav>
  )
}

/** Returns `value` only after it has stopped changing for `delayMs`; used to avoid a request per keystroke. */
export function useDebouncedValue<T>(value: T, delayMs: number): T {
  const [debounced, setDebounced] = useState(value)
  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delayMs)
    return () => clearTimeout(timer)
  }, [value, delayMs])
  return debounced
}

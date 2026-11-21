import { describeError } from '../../shared/api/client'
import type { Shift } from '../../shared/api/types'
import { useServerNow, useServerNowReader } from '../../shared/time/serverClock'
import { formatClock, formatLongDate, formatTime } from '../../shared/time/zurich'
import { Notice, Skeleton } from '../../shared/ui/components'
import { useShiftAction } from './shiftActions'
import type { ShiftAction } from './shiftActions'

/** Only this small component re-renders every second; the rest of the dashboard does not. */
function LiveClock() {
  const { nowMs, isUnavailable } = useServerNow(1000)

  if (nowMs === null) {
    return (
      <div className="clock">
        {isUnavailable ? (
          <Notice>השעה משירות הזמן החיצוני אינה זמינה כרגע, ולכן גם לא ניתן לרשום כניסה או יציאה. ננסה שוב בעוד דקה.</Notice>
        ) : (
          <Skeleton width={220} height={56} />
        )}
      </div>
    )
  }

  return (
    <div className="clock">
      {/* The ticking digits are hidden from screen readers; reading every second would be noise. */}
      <span className="clock-time latin" aria-hidden="true">
        {formatClock(nowMs)}
      </span>
      <span className="visually-hidden">השעה בציריך {formatTime(nowMs)}</span>
      <span className="muted">{formatLongDate(nowMs)} · שעון ציריך</span>
    </div>
  )
}

export function ClockCard({ shift, isLoading }: { shift: Shift | null; isLoading: boolean }) {
  const action = useShiftAction()
  const readDisplayNow = useServerNowReader()
  const pause = shift?.break ?? null
  const isOnBreak = pause !== null && pause.endUtc === null
  // One break per shift: once it has ended, clock-out is the only action left.
  const hadBreak = pause !== null && pause.endUtc !== null

  function run(next: ShiftAction) {
    action.mutate({ action: next, idempotencyKey: crypto.randomUUID(), displayNowMs: readDisplayNow() })
  }

  return (
    <section className="card clock-card" aria-labelledby="clock-title">
      <h2 id="clock-title">שעון נוכחות</h2>
      <LiveClock />

      {isLoading ? (
        <Skeleton height={56} />
      ) : shift && hadBreak ? (
        <div className="stack">
          <p className="muted">
            במשמרת מאז <span className="latin">{formatTime(shift.clockInUtc)}</span> · ההפסקה של המשמרת נוצלה (
            <span className="latin">
              {formatTime(pause.startUtc)}–{formatTime(pause.endUtc ?? pause.startUtc)}
            </span>
            )
          </p>
          <button type="button" className="button button-dark button-large" disabled={action.isPending} onClick={() => run('clockOut')}>
            יציאה
          </button>
        </div>
      ) : shift ? (
        <div className="stack">
          <p className="muted">
            {isOnBreak ? 'בהפסקה מאז ' : 'במשמרת מאז '}
            <span className="latin">{formatTime(isOnBreak ? pause.startUtc : shift.clockInUtc)}</span>
          </p>
          <div className="clock-actions">
            <button type="button" className="button button-dark button-large" disabled={action.isPending} onClick={() => run('clockOut')}>
              יציאה
            </button>
            <button
              type="button"
              className="button button-large"
              disabled={action.isPending}
              onClick={() => run(isOnBreak ? 'breakEnd' : 'breakStart')}
            >
              {isOnBreak ? 'סיום הפסקה' : 'התחלת הפסקה'}
            </button>
          </div>
        </div>
      ) : (
        <button type="button" className="button button-primary button-large" disabled={action.isPending} onClick={() => run('clockIn')}>
          כניסה
        </button>
      )}

      <div aria-live="polite">
        {action.isPending && <p className="muted">רושם לפי שעון חיצוני…</p>}
        {action.isError && <Notice>{describeError(action.error)}</Notice>}
      </div>
    </section>
  )
}

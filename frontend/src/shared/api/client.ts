// One small fetch wrapper for the whole app. It knows three things about the API:
//  - cookies carry the session, so every call is same-origin with credentials;
//  - state-changing calls need the CSRF token in a header (fetched once, refreshed when rejected);
//  - errors arrive as RFC 9457 problems with a stable `code`.

const BASE = '/api/v1'
const SAFE_METHODS = new Set(['GET', 'QUERY'])

type Method = 'GET' | 'POST' | 'PATCH' | 'DELETE' | 'QUERY'

export class ApiError extends Error {
  status: number
  code: string
  fieldErrors: Record<string, string[]>
  /** From the Retry-After header, when the server sent one. */
  retryAfterSeconds: number | null

  constructor(status: number, code: string, title: string, fieldErrors: Record<string, string[]> = {}, retryAfterSeconds: number | null = null) {
    super(title)
    this.status = status
    this.code = code
    this.fieldErrors = fieldErrors
    this.retryAfterSeconds = retryAfterSeconds
  }
}

interface RequestOptions {
  body?: unknown
  /** One key per user action; retries of that action reuse it, so the server records it once. */
  idempotencyKey?: string
  signal?: AbortSignal
}

let csrfToken: string | null = null

/** The token is bound to the session, so it must be fetched again after sign-in and sign-out. */
export function forgetCsrfToken(): void {
  csrfToken = null
}

async function getCsrfToken(): Promise<string> {
  if (csrfToken === null) {
    const response = await fetch(`${BASE}/auth/csrf`, { credentials: 'same-origin' })
    csrfToken = ((await response.json()) as { token: string }).token
  }
  return csrfToken
}

async function send(method: Method, path: string, options: RequestOptions): Promise<Response> {
  const headers: Record<string, string> = {}
  if (options.body !== undefined) {
    headers['Content-Type'] = 'application/json'
  }
  if (options.idempotencyKey) {
    headers['Idempotency-Key'] = options.idempotencyKey
  }
  if (!SAFE_METHODS.has(method)) {
    headers['X-CSRF-Token'] = await getCsrfToken()
  }
  return fetch(BASE + path, {
    method,
    headers,
    body: options.body === undefined ? undefined : JSON.stringify(options.body),
    credentials: 'same-origin',
    signal: options.signal,
  })
}

async function toError(response: Response): Promise<ApiError> {
  const retryAfter = Number(response.headers.get('Retry-After'))
  const retryAfterSeconds = Number.isFinite(retryAfter) && retryAfter > 0 ? retryAfter : null
  try {
    const problem = (await response.json()) as { code?: string; title?: string; errors?: Record<string, string[]> }
    return new ApiError(response.status, problem.code ?? 'unknown', problem.title ?? response.statusText, problem.errors, retryAfterSeconds)
  } catch {
    return new ApiError(response.status, 'unknown', response.statusText, {}, retryAfterSeconds)
  }
}

export async function api<T>(method: Method, path: string, options: RequestOptions = {}): Promise<T> {
  let response = await send(method, path, options)
  if (response.status === 403 && !SAFE_METHODS.has(method)) {
    const error = await toError(response.clone())
    if (error.code === 'csrf_invalid') {
      forgetCsrfToken()
      response = await send(method, path, options)
    }
  }
  if (!response.ok) {
    throw await toError(response)
  }
  return (response.status === 204 ? undefined : await response.json()) as T
}

/** Hebrew message for an error the user can act on. */
export function describeError(error: unknown): string {
  const messages: Record<string, string> = {
    time_source_unavailable: 'שירות הזמן החיצוני אינו זמין כרגע, ולכן שום דבר לא נרשם. נסו שוב בעוד רגע.',
    shift_already_open: 'כבר קיימת משמרת פתוחה.',
    no_open_shift: 'אין משמרת פתוחה.',
    break_already_open: 'כבר קיימת הפסקה פתוחה.',
    break_already_taken: 'כבר יצאת להפסקה במשמרת הזו. אפשר לצאת להפסקה אחת בלבד בכל משמרת.',
    no_open_break: 'אין הפסקה פתוחה.',
    rate_limited: 'בוצעו יותר מדי בקשות. המתינו דקה ונסו שוב.',
    stale_version: 'המשמרת השתנתה מאז שנטענה. רעננו ונסו שוב.',
    shift_overlap: 'הזמנים החדשים חופפים למשמרת אחרת של העובד.',
    validation_failed: 'הנתונים שהוזנו אינם תקינים.',
    forbidden: 'הפעולה מותרת למנהלים בלבד.',
    unauthenticated: 'יש להתחבר מחדש.',
    invalid_credentials: 'שם המשתמש או הסיסמה שגויים. לשם משתמש שכבר נכנס צריך להזין את הסיסמה מהכניסה הראשונה שלו.',
  }
  const code = error instanceof ApiError ? error.code : ''
  if (code === 'sign_in_locked' && error instanceof ApiError) {
    const minutes = Math.max(1, Math.ceil((error.retryAfterSeconds ?? 60) / 60))
    const wait = minutes === 1 ? 'דקה' : `${minutes} דקות`
    return `היו יותר מדי ניסיונות עם סיסמה שגויה לשם המשתמש הזה, ולכן הכניסה אליו נחסמה זמנית. נסו שוב בעוד ${wait}.`
  }
  return messages[code] ?? 'משהו השתבש. נסו שוב.'
}

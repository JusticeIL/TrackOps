import { useState } from 'react'
import type { FormEvent } from 'react'
import { Navigate } from 'react-router-dom'
import { ApiError, describeError } from '../../shared/api/client'
import { Logo, Notice } from '../../shared/ui/components'
import { useSession, useSignIn } from './session'
import './auth.css'

export default function SignInPage() {
  const session = useSession()
  const signIn = useSignIn()
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')

  if (session.data) {
    return <Navigate to="/" replace />
  }

  const fieldErrors = signIn.error instanceof ApiError ? signIn.error.fieldErrors : {}

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    signIn.mutate({ username, password })
  }

  return (
    <main className="sign-in">
      <form className="card sign-in-card stack" onSubmit={handleSubmit} noValidate>
        <div className="sign-in-brand">
          <Logo size={48} />
          <h1>ברוכים הבאים ל-<span className="latin">TrackOps</span></h1>
          <p className="muted">התחברו כדי להמשיך</p>
        </div>

        <div className="field">
          <label htmlFor="username">שם משתמש</label>
          <input
            id="username"
            className="input latin"
            autoComplete="username"
            value={username}
            onChange={(event) => setUsername(event.target.value)}
            aria-invalid={fieldErrors.username ? true : undefined}
            aria-describedby="username-hint"
            required
          />
          <span id="username-hint" className="muted">
            3–32 תווים באנגלית, ספרות, נקודה, מקף או קו תחתון. המשתמש <span className="latin">admin</span> הוא מנהל.
          </span>
          {fieldErrors.username && <span className="field-error">שם המשתמש אינו תקין.</span>}
        </div>

        <div className="field">
          <label htmlFor="password">סיסמה</label>
          <input
            id="password"
            className="input latin"
            type="password"
            autoComplete="current-password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            aria-invalid={fieldErrors.password ? true : undefined}
            required
          />
          {fieldErrors.password && <span className="field-error">הסיסמה חייבת להכיל 8 תווים לפחות.</span>}
        </div>

        {signIn.isError && Object.keys(fieldErrors).length === 0 && <Notice kind="error">{describeError(signIn.error)}</Notice>}

        <button type="submit" className="button button-primary button-large" disabled={signIn.isPending}>
          {signIn.isPending ? 'מתחבר…' : 'כניסה'}
        </button>
      </form>
    </main>
  )
}

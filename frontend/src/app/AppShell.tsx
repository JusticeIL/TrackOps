import { Suspense, useEffect, useRef, useState } from 'react'
import { Link, NavLink, Outlet } from 'react-router-dom'
import { useSession, useSignOut } from '../features/auth/session'
import { Logo, SkeletonRows } from '../shared/ui/components'
import { CalendarIcon, HistoryIcon, HomeIcon, SignOutIcon, UsersIcon } from '../shared/ui/icons'

const NAV_ITEMS = [
  { to: '/', label: 'מסך הבית', icon: HomeIcon, managerOnly: false },
  { to: '/history', label: 'היסטוריית משמרות', icon: HistoryIcon, managerOnly: false },
  { to: '/calendar', label: 'לוח שנה', icon: CalendarIcon, managerOnly: false },
  { to: '/employees', label: 'עובדים', icon: UsersIcon, managerOnly: true },
]

function UserMenu() {
  const session = useSession()
  const signOut = useSignOut()
  const [isOpen, setIsOpen] = useState(false)
  const menuRef = useRef<HTMLDivElement>(null)
  const user = session.data

  useEffect(() => {
    if (!isOpen) {
      return
    }
    function handleKey(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        setIsOpen(false)
      }
    }
    function handleClick(event: MouseEvent) {
      if (menuRef.current && !menuRef.current.contains(event.target as Node)) {
        setIsOpen(false)
      }
    }
    document.addEventListener('keydown', handleKey)
    document.addEventListener('mousedown', handleClick)
    return () => {
      document.removeEventListener('keydown', handleKey)
      document.removeEventListener('mousedown', handleClick)
    }
  }, [isOpen])

  if (!user) {
    return null
  }

  return (
    <div className="user-menu" ref={menuRef}>
      <button type="button" className="button" aria-expanded={isOpen} aria-controls="user-menu-panel" onClick={() => setIsOpen(!isOpen)}>
        <span className="avatar" aria-hidden="true">
          {user.displayName.slice(0, 2).toUpperCase()}
        </span>
        <span className="latin">{user.displayName}</span>
      </button>
      {isOpen && (
        <div id="user-menu-panel" className="card user-menu-panel stack">
          <span className="muted">{user.role === 'Manager' ? 'מנהל/ת' : 'עובד/ת'}</span>
          <button type="button" className="button button-primary" onClick={() => signOut.mutate()} disabled={signOut.isPending}>
            <SignOutIcon />
            התנתקות
          </button>
        </div>
      )}
    </div>
  )
}

export function AppShell() {
  const session = useSession()
  const isManager = session.data?.role === 'Manager'

  return (
    <div className="shell">
      <a className="skip-link" href="#main">
        דילוג לתוכן הראשי
      </a>
      <header className="topbar">
        <Link to="/" className="brand">
          <Logo />
          <span className="latin">TrackOps</span>
        </Link>
        <UserMenu />
      </header>
      <nav className="sidebar" aria-label="ניווט ראשי">
        <ul>
          {NAV_ITEMS.filter((item) => !item.managerOnly || isManager).map((item) => (
            <li key={item.to}>
              <NavLink to={item.to} end={item.to === '/'} className="nav-link">
                <item.icon />
                {item.label}
              </NavLink>
            </li>
          ))}
        </ul>
      </nav>
      <main className="main" id="main" tabIndex={-1}>
        <Suspense fallback={<SkeletonRows rows={6} />}>
          <Outlet />
        </Suspense>
      </main>
    </div>
  )
}

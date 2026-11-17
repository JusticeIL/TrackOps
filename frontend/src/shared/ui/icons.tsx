// Small line icons. They are decoration next to a text label, so screen readers skip them.
// The colour comes from the surrounding text colour (currentColor) or from a CSS class.
import type { ReactNode } from 'react'

function Icon({ children }: { children: ReactNode }) {
  return (
    <svg
      className="icon"
      width="20"
      height="20"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
    >
      {children}
    </svg>
  )
}

export function HomeIcon() {
  return (
    <Icon>
      <path d="M3 11 12 3l9 8" />
      <path d="M5 10v11h14V10" />
    </Icon>
  )
}

export function HistoryIcon() {
  return (
    <Icon>
      <circle cx="12" cy="12" r="9" />
      <path d="M12 7v5l3.5 2" />
    </Icon>
  )
}

export function CalendarIcon() {
  return (
    <Icon>
      <rect x="3" y="5" width="18" height="16" rx="2" />
      <path d="M3 10h18M8 3v4M16 3v4" />
    </Icon>
  )
}

export function ChartIcon() {
  return (
    <Icon>
      <path d="M3 21h18M7 21V11M12 21V5M17 21v-7" />
    </Icon>
  )
}

export function DocumentIcon() {
  return (
    <Icon>
      <path d="M6 3h8l4 4v14H6z" />
      <path d="M14 3v4h4M9 13h6M9 17h6" />
    </Icon>
  )
}

export function PlusIcon() {
  return (
    <Icon>
      <path d="M12 5v14M5 12h14" />
    </Icon>
  )
}

export function SickIcon() {
  return (
    <Icon>
      <path d="M4 5h16v11H9l-5 4z" />
      <path d="M12 8v5M9.5 10.5h5" />
    </Icon>
  )
}

export function UsersIcon() {
  return (
    <Icon>
      <circle cx="9" cy="8" r="3.5" />
      <path d="M2.5 20c0-4 3-6 6.5-6s6.5 2 6.5 6" />
      <circle cx="17.5" cy="9" r="2.5" />
      <path d="M18 14.2c2.2.6 3.5 2.4 3.5 5.8" />
    </Icon>
  )
}

export function SignOutIcon() {
  return (
    <Icon>
      <path d="M10 4H5v16h5" />
      <path d="M14 8l4 4-4 4M18 12H9" />
    </Icon>
  )
}

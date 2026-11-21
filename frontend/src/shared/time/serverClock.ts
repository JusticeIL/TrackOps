// The display clock.
//
// It is seeded with the time the server got from the external time API, then kept ticking with
// performance.now(), which is monotonic: it never jumps when the computer's clock is changed.
// The seed is refreshed every minute. The computer's own clock is not read anywhere.
//
// This clock is for display only. Recording a clock-in or clock-out never uses it: the server
// asks the external time API again for every recording.
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useCallback, useEffect, useState } from 'react'
import { api } from '../api/client'
import type { ServerTime } from '../api/types'

const RESYNC_MS = 60_000
const CLOCK_KEY = ['server-time'] as const

interface ClockAnchor {
  serverMs: number
  monotonicMs: number
}

const readAnchor = (anchor: ClockAnchor | undefined): number | null =>
  anchor ? anchor.serverMs + (performance.now() - anchor.monotonicMs) : null

/** Returns a function that reads the display clock once, without making the caller re-render on every tick. */
export function useServerNowReader(): () => number | null {
  const queryClient = useQueryClient()
  return useCallback(() => readAnchor(queryClient.getQueryData<ClockAnchor>(CLOCK_KEY)), [queryClient])
}

async function fetchAnchor(): Promise<ClockAnchor> {
  const sentAt = performance.now()
  const time = await api<ServerTime>('GET', '/time')
  const receivedAt = performance.now()
  // Assume the answer spent half of the round trip on its way back.
  return { serverMs: Date.parse(time.utc) + (receivedAt - sentAt) / 2, monotonicMs: receivedAt }
}

interface ServerNow {
  /** Current server time in milliseconds; null until the first sync. */
  nowMs: number | null
  /** True when there is no time yet because the server could not get one from the time service. */
  isUnavailable: boolean
}

/**
 * Current server time, re-rendering the caller every `tickMs`. If the server cannot reach the
 * time service, the clock says so (and tries again every minute) instead of loading forever.
 */
export function useServerNow(tickMs: number): ServerNow {
  const { data: anchor, isError } = useQuery({
    queryKey: CLOCK_KEY,
    queryFn: fetchAnchor,
    refetchInterval: RESYNC_MS,
    staleTime: RESYNC_MS,
  })
  const [, setTick] = useState(0)

  useEffect(() => {
    const timer = setInterval(() => setTick((tick) => tick + 1), tickMs)
    return () => clearInterval(timer)
  }, [tickMs])

  // After one good sync the clock keeps ticking from it even if a later re-sync fails.
  return { nowMs: readAnchor(anchor), isUnavailable: isError && !anchor }
}

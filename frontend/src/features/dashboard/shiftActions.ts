// The four recording actions, with optimistic updates.
//
// When the user clicks, the screen changes at once (the optimistic update). If the server then
// refuses or cannot record, the previous state is put back (the rollback) and the reason is shown.
// The time shown during that moment is only the display clock; the recorded time is whatever the
// server reads from the external time API, and it replaces the placeholder as soon as it arrives.
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, ApiError } from '../../shared/api/client'
import type { Shift } from '../../shared/api/types'

export const CURRENT_SHIFT_KEY = ['shift', 'current'] as const

export type ShiftAction = 'clockIn' | 'clockOut' | 'breakStart' | 'breakEnd'

const PATHS: Record<ShiftAction, string> = {
  clockIn: '/shifts',
  clockOut: '/shifts/current/clock-out',
  breakStart: '/shifts/current/breaks/current/start',
  breakEnd: '/shifts/current/breaks/current/end',
}

interface ActionRequest {
  action: ShiftAction
  /** Made once per click. Automatic retries reuse it, so the server records the click once. */
  idempotencyKey: string
  /** Display-clock time, used only to draw the optimistic placeholder. */
  displayNowMs: number | null
}

export function useCurrentShift() {
  return useQuery({
    queryKey: CURRENT_SHIFT_KEY,
    queryFn: async () => (await api<Shift | undefined>('GET', '/shifts/current')) ?? null,
  })
}

function optimisticShift(previous: Shift | null | undefined, request: ActionRequest): Shift | null {
  const nowIso = new Date(request.displayNowMs ?? 0).toISOString()
  const base: Shift = previous ?? {
    id: -1, userId: -1, username: '', displayName: '', clockInUtc: nowIso, clockOutUtc: null, status: 'Open',
    isCorrected: false, rowVersion: '', shiftMinutes: null, breakMinutes: null, workedMinutes: null, breaks: [],
  }
  switch (request.action) {
    case 'clockIn':
      return base
    case 'clockOut':
      return null
    case 'breakStart':
      return { ...base, breaks: [...base.breaks, { id: -1, startUtc: nowIso, endUtc: null, minutes: null }] }
    case 'breakEnd':
      return { ...base, breaks: base.breaks.map((pause) => (pause.endUtc === null ? { ...pause, endUtc: nowIso } : pause)) }
  }
}

export function useShiftAction() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: ActionRequest) =>
      api<Shift>('POST', PATHS[request.action], { idempotencyKey: request.idempotencyKey }),
    // A request that got no answer may or may not have been recorded. Sending it again with the same
    // key is safe: the server replays the stored result instead of recording twice.
    retry: (failureCount, error) => !(error instanceof ApiError) && failureCount < 2,
    onMutate: async (request) => {
      await queryClient.cancelQueries({ queryKey: CURRENT_SHIFT_KEY })
      const previous = queryClient.getQueryData<Shift | null>(CURRENT_SHIFT_KEY)
      queryClient.setQueryData(CURRENT_SHIFT_KEY, optimisticShift(previous, request))
      return { previous }
    },
    onError: (_error, _request, context) => {
      queryClient.setQueryData(CURRENT_SHIFT_KEY, context?.previous ?? null)
    },
    onSuccess: (shift, request) => {
      queryClient.setQueryData(CURRENT_SHIFT_KEY, request.action === 'clockOut' ? null : shift)
    },
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: ['activity'] })
      void queryClient.invalidateQueries({ queryKey: ['calendar'] })
      void queryClient.invalidateQueries({ queryKey: ['history'] })
    },
  })
}

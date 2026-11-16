import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { QueryClient } from '@tanstack/react-query'
import { api, ApiError, forgetCsrfToken } from '../../shared/api/client'
import type { User } from '../../shared/api/types'

export const SESSION_KEY = ['session'] as const

/** The signed-in user, or null. The cookies are HttpOnly, so the only way to know is to ask the server. */
export function useSession() {
  return useQuery({
    queryKey: SESSION_KEY,
    queryFn: async (): Promise<User | null> => {
      try {
        return await api<User>('GET', '/auth/session')
      } catch (error) {
        if (error instanceof ApiError && error.status === 401) {
          return null
        }
        throw error
      }
    },
    staleTime: 5 * 60_000,
  })
}

/**
 * Switches the app to another user (or to nobody).
 * The session entry is updated in place, so every component watching it re-renders, and the
 * route guard redirects. Everything else in the cache belonged to the previous user and is dropped.
 * (Clearing the whole cache instead would detach the guard from the session and leave the old
 * user's screen visible.)
 */
function switchUser(queryClient: QueryClient, user: User | null): void {
  forgetCsrfToken()
  queryClient.setQueryData(SESSION_KEY, user)
  queryClient.removeQueries({ predicate: (query) => query.queryKey[0] !== SESSION_KEY[0] })
}

export function useSignIn() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (credentials: { username: string; password: string }) => api<User>('POST', '/auth/session', { body: credentials }),
    onSuccess: (user) => switchUser(queryClient, user),
  })
}

export function useSignOut() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => api<void>('DELETE', '/auth/session'),
    // Even if the request failed, show the sign-in screen; the next request will tell the truth.
    onSettled: () => switchUser(queryClient, null),
  })
}

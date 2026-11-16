import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
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

export function useSignIn() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (credentials: { username: string; password: string }) => api<User>('POST', '/auth/session', { body: credentials }),
    onSuccess: (user) => {
      forgetCsrfToken()
      queryClient.clear()
      queryClient.setQueryData(SESSION_KEY, user)
    },
  })
}

export function useSignOut() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => api<void>('DELETE', '/auth/session'),
    onSettled: () => {
      forgetCsrfToken()
      queryClient.clear()
      queryClient.setQueryData(SESSION_KEY, null)
    },
  })
}

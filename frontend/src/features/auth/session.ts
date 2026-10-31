import { useMutation, useQueryClient } from '@tanstack/react-query';
import { createContext, use } from 'react';
import { useNavigate } from 'react-router';
import { ApiError, apiRequest } from '@/lib/api';
import type { CurrentUser } from '@/types/api';

export const meQueryKey = ['auth', 'me'] as const;

export async function fetchMe(): Promise<CurrentUser | null> {
  try {
    return await apiRequest<CurrentUser>('/auth/me', { skipUnauthorizedHandler: true });
  } catch (error) {
    if (error instanceof ApiError && error.isUnauthorized) return null;
    throw error;
  }
}

export interface AuthContextValue {
  user: CurrentUser | null;
  isLoading: boolean;
}

export const AuthContext = createContext<AuthContextValue | null>(null);

export function useAuth(): AuthContextValue {
  const context = use(AuthContext);
  if (!context) throw new Error('useAuth must be used inside AuthProvider');
  return context;
}

/** Convenience accessor for pages rendered behind RequireAuth. */
export function useCurrentUser(): CurrentUser {
  const { user } = useAuth();
  if (!user) throw new Error('useCurrentUser requires an authenticated route');
  return user;
}

export function useLogin() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (credentials: { email: string; password: string }) =>
      apiRequest<CurrentUser>('/auth/login', { method: 'POST', body: credentials, skipUnauthorizedHandler: true }),
    onSuccess: (user) => {
      queryClient.clear();
      queryClient.setQueryData(meQueryKey, user);
    },
  });
}

export function useLogout() {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  return useMutation({
    mutationFn: () => apiRequest<undefined>('/auth/logout', { method: 'POST', skipUnauthorizedHandler: true }),
    onSettled: () => {
      queryClient.clear();
      queryClient.setQueryData(meQueryKey, null);
      void navigate('/login', { replace: true });
    },
  });
}

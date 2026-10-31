import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest, toQueryString } from '@/lib/api';
import type { PagedResult, Role, User } from '@/types/api';

export const userKeys = { all: ['users'] as const };

export function useUsers(page: number) {
  return useQuery({
    queryKey: [...userKeys.all, page],
    queryFn: ({ signal }) =>
      apiRequest<PagedResult<User>>(`/users${toQueryString({ page, pageSize: 25 })}`, { signal }),
    placeholderData: keepPreviousData,
  });
}

function useUserInvalidation() {
  const queryClient = useQueryClient();
  return () => queryClient.invalidateQueries({ queryKey: userKeys.all });
}

export function useCreateUser() {
  const invalidate = useUserInvalidation();
  return useMutation({
    mutationFn: (input: { email: string; displayName: string; role: Role; password: string }) =>
      apiRequest<User>('/users', { method: 'POST', body: input }),
    onSuccess: invalidate,
  });
}

export function useChangeRole() {
  const invalidate = useUserInvalidation();
  return useMutation({
    mutationFn: ({ id, role }: { id: string; role: Role }) =>
      apiRequest<undefined>(`/users/${id}/role`, { method: 'PUT', body: { role } }),
    onSuccess: invalidate,
  });
}

export function useSetUserActive() {
  const invalidate = useUserInvalidation();
  return useMutation({
    mutationFn: ({ id, isActive }: { id: string; isActive: boolean }) =>
      apiRequest<undefined>(`/users/${id}/status`, { method: 'PUT', body: { isActive } }),
    onSuccess: invalidate,
  });
}

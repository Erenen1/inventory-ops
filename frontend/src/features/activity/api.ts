import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { apiRequest, toQueryString } from '@/lib/api';
import type { ActivityEntry, PagedResult } from '@/types/api';

export function useActivity(params: { module?: string; page: number; pageSize: number }, enabled = true) {
  return useQuery({
    queryKey: ['activity', params],
    queryFn: ({ signal }) => apiRequest<PagedResult<ActivityEntry>>(`/activity${toQueryString(params)}`, { signal }),
    placeholderData: keepPreviousData,
    // The feed is projected asynchronously from events; polling surfaces new entries.
    refetchInterval: 15_000,
    enabled,
  });
}

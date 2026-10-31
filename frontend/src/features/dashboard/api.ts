import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import type { DashboardSummary } from '@/types/api';

export function useDashboardSummary() {
  return useQuery({
    queryKey: ['dashboard', 'summary'],
    queryFn: ({ signal }) => apiRequest<DashboardSummary>('/dashboard/summary', { signal }),
    refetchInterval: 60_000,
  });
}

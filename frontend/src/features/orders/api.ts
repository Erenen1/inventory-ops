import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest, toQueryString } from '@/lib/api';
import type { OrderDetails, OrderListItem, OrderStatus, PagedResult, SortDirection } from '@/types/api';

export interface OrderListParams {
  search?: string;
  status?: OrderStatus;
  sortBy: string;
  sortDirection: SortDirection;
  page: number;
  pageSize: number;
}

export interface SubmitOrderInput {
  customerName: string;
  notes: string | null;
  lines: { productId: string; quantity: number }[];
}

export const orderKeys = {
  all: ['orders'] as const,
  list: (params: OrderListParams) => ['orders', 'list', params] as const,
  detail: (id: string) => ['orders', 'detail', id] as const,
};

export function useOrders(params: OrderListParams) {
  return useQuery({
    queryKey: orderKeys.list(params),
    queryFn: ({ signal }) =>
      apiRequest<PagedResult<OrderListItem>>(`/orders${toQueryString({ ...params })}`, { signal }),
    placeholderData: keepPreviousData,
  });
}

export function useOrder(id: string) {
  return useQuery({
    queryKey: orderKeys.detail(id),
    queryFn: ({ signal }) => apiRequest<OrderDetails>(`/orders/${id}`, { signal }),
  });
}

function useOrderInvalidation() {
  const queryClient = useQueryClient();
  return () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: orderKeys.all }),
      queryClient.invalidateQueries({ queryKey: ['inventory'] }),
      queryClient.invalidateQueries({ queryKey: ['products'] }),
      queryClient.invalidateQueries({ queryKey: ['dashboard'] }),
      queryClient.invalidateQueries({ queryKey: ['activity'] }),
    ]);
}

/**
 * The idempotency key is created once per order form, so retries and double submits of the same
 * form can never create a second order.
 */
export function useSubmitOrder(idempotencyKey: string) {
  const invalidate = useOrderInvalidation();
  return useMutation({
    mutationFn: (input: SubmitOrderInput) =>
      apiRequest<OrderDetails>('/orders', {
        method: 'POST',
        body: input,
        headers: { 'Idempotency-Key': idempotencyKey },
      }),
    onSuccess: invalidate,
  });
}

export type OrderAction = 'process' | 'fulfill' | 'cancel';

export function useOrderTransition(id: string) {
  const invalidate = useOrderInvalidation();
  return useMutation({
    mutationFn: ({ action, reason }: { action: OrderAction; reason?: string }) =>
      apiRequest<undefined>(`/orders/${id}/${action}`, {
        method: 'POST',
        body: action === 'cancel' ? { reason: reason?.trim() ? reason.trim() : null } : undefined,
      }),
    onSuccess: invalidate,
  });
}

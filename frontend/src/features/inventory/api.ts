import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest, toQueryString } from '@/lib/api';
import type { AdjustmentReason, InventoryItem, PagedResult, SortDirection, StockMovement } from '@/types/api';

export interface InventoryListParams {
  search?: string;
  lowStockOnly?: boolean;
  sortBy: string;
  sortDirection: SortDirection;
  page: number;
  pageSize: number;
}

export const inventoryKeys = {
  all: ['inventory'] as const,
  list: (params: InventoryListParams) => ['inventory', 'list', params] as const,
  item: (productId: string) => ['inventory', 'item', productId] as const,
  movements: (productId: string, page: number) => ['inventory', 'movements', productId, page] as const,
};

export function useInventory(params: InventoryListParams) {
  return useQuery({
    queryKey: inventoryKeys.list(params),
    queryFn: ({ signal }) =>
      apiRequest<PagedResult<InventoryItem>>(`/inventory${toQueryString({ ...params })}`, { signal }),
    placeholderData: keepPreviousData,
  });
}

export function useInventoryItem(productId: string) {
  return useQuery({
    queryKey: inventoryKeys.item(productId),
    queryFn: ({ signal }) => apiRequest<InventoryItem>(`/inventory/${productId}`, { signal }),
  });
}

export function useStockMovements(productId: string, page: number) {
  return useQuery({
    queryKey: inventoryKeys.movements(productId, page),
    queryFn: ({ signal }) =>
      apiRequest<PagedResult<StockMovement>>(
        `/inventory/${productId}/movements${toQueryString({ page, pageSize: 10 })}`,
        { signal },
      ),
    placeholderData: keepPreviousData,
  });
}

function useStockInvalidation() {
  const queryClient = useQueryClient();
  return () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: inventoryKeys.all }),
      queryClient.invalidateQueries({ queryKey: ['products'] }),
      queryClient.invalidateQueries({ queryKey: ['dashboard'] }),
    ]);
}

export function useAdjustStock(productId: string) {
  const invalidate = useStockInvalidation();
  return useMutation({
    mutationFn: (input: { quantityChange: number; reason: AdjustmentReason; note: string | null }) =>
      apiRequest<undefined>(`/inventory/${productId}/adjustments`, { method: 'POST', body: input }),
    onSuccess: invalidate,
  });
}

export function useSetReorderThreshold(productId: string) {
  const invalidate = useStockInvalidation();
  return useMutation({
    mutationFn: (reorderThreshold: number) =>
      apiRequest<undefined>(`/inventory/${productId}/reorder-threshold`, { method: 'PUT', body: { reorderThreshold } }),
    onSuccess: invalidate,
  });
}

export const adjustmentReasons: { value: AdjustmentReason; label: string }[] = [
  { value: 'Received', label: 'Received from supplier' },
  { value: 'Returned', label: 'Customer return' },
  { value: 'CountCorrection', label: 'Stock count correction' },
  { value: 'Damaged', label: 'Damaged' },
  { value: 'Lost', label: 'Lost / missing' },
  { value: 'Other', label: 'Other (note required)' },
];

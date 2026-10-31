import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest, toQueryString } from '@/lib/api';
import type { Category, PagedResult, ProductDetails, ProductListItem, SortDirection } from '@/types/api';

export interface ProductListParams {
  search?: string;
  categoryId?: string;
  isActive?: boolean;
  sortBy: string;
  sortDirection: SortDirection;
  page: number;
  pageSize: number;
}

export interface ProductInput {
  name: string;
  description: string | null;
  categoryId: string;
  unitPrice: number;
  currency: string;
}

export const productKeys = {
  all: ['products'] as const,
  list: (params: ProductListParams) => ['products', 'list', params] as const,
  detail: (id: string) => ['products', 'detail', id] as const,
  categories: ['categories'] as const,
};

export function useProducts(params: ProductListParams) {
  return useQuery({
    queryKey: productKeys.list(params),
    queryFn: ({ signal }) =>
      apiRequest<PagedResult<ProductListItem>>(`/products${toQueryString({ ...params })}`, { signal }),
    placeholderData: keepPreviousData,
  });
}

export function useProduct(id: string) {
  return useQuery({
    queryKey: productKeys.detail(id),
    queryFn: ({ signal }) => apiRequest<ProductDetails>(`/products/${id}`, { signal }),
  });
}

export function useCategories() {
  return useQuery({
    queryKey: productKeys.categories,
    queryFn: ({ signal }) => apiRequest<Category[]>('/categories', { signal }),
    staleTime: 5 * 60_000,
  });
}

/** Writes invalidate every view that shows catalog or stock data; no optimistic updates. */
function useCatalogInvalidation() {
  const queryClient = useQueryClient();
  return () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: productKeys.all }),
      queryClient.invalidateQueries({ queryKey: ['inventory'] }),
      queryClient.invalidateQueries({ queryKey: ['dashboard'] }),
      queryClient.invalidateQueries({ queryKey: productKeys.categories }),
    ]);
}

export function useCreateProduct() {
  const invalidate = useCatalogInvalidation();
  return useMutation({
    mutationFn: (input: ProductInput & { sku: string; reorderThreshold: number }) =>
      apiRequest<{ id: string }>('/products', { method: 'POST', body: input }),
    onSuccess: invalidate,
  });
}

export function useUpdateProduct(id: string) {
  const invalidate = useCatalogInvalidation();
  return useMutation({
    mutationFn: (input: ProductInput) => apiRequest<undefined>(`/products/${id}`, { method: 'PUT', body: input }),
    onSuccess: invalidate,
  });
}

export function useSetProductActive(id: string) {
  const invalidate = useCatalogInvalidation();
  return useMutation({
    mutationFn: (active: boolean) =>
      apiRequest<undefined>(`/products/${id}/${active ? 'activate' : 'deactivate'}`, { method: 'POST' }),
    onSuccess: invalidate,
  });
}

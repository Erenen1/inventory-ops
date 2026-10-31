import { useCallback, useEffect, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router';
import type { SortDirection } from '@/types/api';

export function useDebouncedValue<T>(value: T, delayMs = 300): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const timer = setTimeout(() => {
      setDebounced(value);
    }, delayMs);
    return () => {
      clearTimeout(timer);
    };
  }, [value, delayMs]);
  return debounced;
}

/**
 * List state (search, filters, sort, page) kept in the URL so views are shareable and survive reloads.
 * Changing any filter resets to page 1.
 */
export function useListParams(defaults: { sortBy: string; sortDirection: SortDirection }) {
  const [params, setParams] = useSearchParams();
  const [search, setSearchInput] = useState(params.get('search') ?? '');
  const debouncedSearch = useDebouncedValue(search.trim());

  const page = Math.max(1, Number(params.get('page') ?? '1') || 1);
  const sortBy = params.get('sortBy') ?? defaults.sortBy;
  const sortDirection = (params.get('sortDirection') as SortDirection | null) ?? defaults.sortDirection;

  const update = useCallback(
    (changes: Record<string, string | null>, resetPage = true) => {
      setParams(
        (current) => {
          const next = new URLSearchParams(current);
          for (const [key, value] of Object.entries(changes)) {
            if (value === null || value === '') next.delete(key);
            else next.set(key, value);
          }
          if (resetPage) next.delete('page');
          return next;
        },
        { replace: true },
      );
    },
    [setParams],
  );

  useEffect(() => {
    if ((params.get('search') ?? '') !== debouncedSearch) {
      update({ search: debouncedSearch });
    }
  }, [debouncedSearch, params, update]);

  const toggleSort = useCallback(
    (field: string) => {
      const direction: SortDirection = sortBy === field && sortDirection === 'Asc' ? 'Desc' : 'Asc';
      update({ sortBy: field, sortDirection: direction });
    },
    [sortBy, sortDirection, update],
  );

  return useMemo(
    () => ({
      search,
      setSearch: setSearchInput,
      appliedSearch: params.get('search') ?? '',
      page,
      setPage: (value: number) => {
        update({ page: String(value) }, false);
      },
      sortBy,
      sortDirection,
      toggleSort,
      filter: (key: string) => params.get(key) ?? '',
      setFilter: (key: string, value: string) => {
        update({ [key]: value });
      },
    }),
    [search, params, page, sortBy, sortDirection, toggleSort, update],
  );
}

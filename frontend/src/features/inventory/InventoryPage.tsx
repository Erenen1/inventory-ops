import { SlidersHorizontal } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router';
import { StockBadge } from '@/components/shared/badges';
import { EmptyState, ErrorState, LoadingRows, PageHeader, Pagination, SearchInput } from '@/components/shared/page';
import { SortableHeader } from '@/components/shared/sortable-header';
import { Button } from '@/components/ui/button';
import { Dialog } from '@/components/ui/overlays';
import { Card, Table, Td, Th } from '@/components/ui/surface';
import { useCurrentUser } from '@/features/auth/session';
import { AdjustStockDialog } from '@/features/inventory/AdjustStockDialog';
import { useInventory } from '@/features/inventory/api';
import { useListParams } from '@/hooks/use-list-params';
import { formatNumber } from '@/lib/format';
import { cn } from '@/lib/utils';
import type { InventoryItem } from '@/types/api';

const PAGE_SIZE = 20;

export function InventoryPage() {
  const user = useCurrentUser();
  const list = useListParams({ sortBy: 'name', sortDirection: 'Asc' });
  const lowStockOnly = list.filter('lowStockOnly') === 'true';
  const [adjusting, setAdjusting] = useState<InventoryItem | null>(null);

  const { data, isLoading, isFetching, error, refetch } = useInventory({
    search: list.appliedSearch,
    lowStockOnly: lowStockOnly || undefined,
    sortBy: list.sortBy,
    sortDirection: list.sortDirection,
    page: list.page,
    pageSize: PAGE_SIZE,
  });
  const sortProps = { sortBy: list.sortBy, sortDirection: list.sortDirection, onSort: list.toggleSort };
  const columns = user.permissions.canOperate ? 8 : 7;

  return (
    <>
      <PageHeader
        title="Inventory"
        description="On-hand, reserved and available stock. Available = on hand − reserved by open orders."
      />
      <Card>
        <div className="flex flex-col gap-3 border-b border-zinc-100 p-4 sm:flex-row sm:items-center">
          <SearchInput
            label="Search inventory"
            placeholder="Search by name or SKU"
            value={list.search}
            onChange={list.setSearch}
          />
          <label className="inline-flex items-center gap-2 text-sm text-zinc-700">
            <input
              type="checkbox"
              className="size-4 rounded border-zinc-300 accent-brand-700"
              checked={lowStockOnly}
              onChange={(e) => {
                list.setFilter('lowStockOnly', e.target.checked ? 'true' : '');
              }}
            />
            Low stock only
          </label>
        </div>

        {error ? (
          <ErrorState error={error} onRetry={() => void refetch()} />
        ) : (
          <>
            <Table aria-busy={isFetching}>
              <thead>
                <tr>
                  <SortableHeader field="sku" label="SKU" {...sortProps} />
                  <SortableHeader field="name" label="Product" {...sortProps} />
                  <SortableHeader field="onHand" label="On hand" align="right" {...sortProps} />
                  <Th className="text-right">Reserved</Th>
                  <SortableHeader field="available" label="Available" align="right" {...sortProps} />
                  <Th className="hidden text-right sm:table-cell">Reorder at</Th>
                  <Th>Level</Th>
                  {user.permissions.canOperate && (
                    <Th className="text-right">
                      <span className="sr-only">Actions</span>
                    </Th>
                  )}
                </tr>
              </thead>
              <tbody className={cn(isFetching && !isLoading && 'opacity-60 transition-opacity')}>
                {isLoading ? (
                  <LoadingRows columns={columns} />
                ) : (
                  data?.items.map((item) => (
                    <tr key={item.productId} className="hover:bg-zinc-50">
                      <Td className="font-mono text-xs text-zinc-600">{item.sku}</Td>
                      <Td>
                        <Link to={`/products/${item.productId}`} className="font-medium text-zinc-900 hover:underline">
                          {item.productName}
                        </Link>
                        <span className="block text-xs text-zinc-500">
                          {item.categoryName}
                          {!item.isActive && ' · inactive'}
                        </span>
                      </Td>
                      <Td className="tabular text-right">{formatNumber(item.onHand)}</Td>
                      <Td className="tabular text-right text-zinc-500">{formatNumber(item.reserved)}</Td>
                      <Td
                        className={cn(
                          'tabular text-right font-medium',
                          item.available === 0 ? 'text-red-600' : item.isLowStock ? 'text-amber-700' : 'text-zinc-900',
                        )}
                      >
                        {formatNumber(item.available)}
                      </Td>
                      <Td className="tabular hidden text-right text-zinc-500 sm:table-cell">
                        {formatNumber(item.reorderThreshold)}
                      </Td>
                      <Td>
                        <StockBadge available={item.available} isLowStock={item.isLowStock} />
                      </Td>
                      {user.permissions.canOperate && (
                        <Td className="text-right">
                          <Button
                            variant="secondary"
                            size="sm"
                            onClick={() => {
                              setAdjusting(item);
                            }}
                            aria-label={`Adjust stock for ${item.sku}`}
                          >
                            <SlidersHorizontal /> Adjust
                          </Button>
                        </Td>
                      )}
                    </tr>
                  ))
                )}
              </tbody>
            </Table>
            {data?.items.length === 0 && (
              <EmptyState
                title={lowStockOnly ? 'No products are low on stock' : 'No inventory records match'}
                description={
                  lowStockOnly ? 'Every product is above its reorder threshold.' : 'Try a different search term.'
                }
              />
            )}
            {data && data.totalCount > 0 && (
              <Pagination
                page={list.page}
                pageSize={PAGE_SIZE}
                totalCount={data.totalCount}
                onPageChange={list.setPage}
                isFetching={isFetching}
              />
            )}
          </>
        )}
      </Card>

      <Dialog
        open={!!adjusting}
        onOpenChange={(open) => {
          if (!open) setAdjusting(null);
        }}
      >
        {adjusting && (
          <AdjustStockDialog
            item={adjusting}
            onDone={() => {
              setAdjusting(null);
            }}
          />
        )}
      </Dialog>
    </>
  );
}

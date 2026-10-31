import { Plus } from 'lucide-react';
import { Link, useNavigate } from 'react-router';
import { OrderStatusBadge } from '@/components/shared/badges';
import { EmptyState, ErrorState, LoadingRows, PageHeader, Pagination, SearchInput } from '@/components/shared/page';
import { SortableHeader } from '@/components/shared/sortable-header';
import { Button } from '@/components/ui/button';
import { Card, Table, Td, Th } from '@/components/ui/surface';
import { useCurrentUser } from '@/features/auth/session';
import { useOrders } from '@/features/orders/api';
import { useListParams } from '@/hooks/use-list-params';
import { formatDateTime, formatMoney, formatNumber } from '@/lib/format';
import { cn } from '@/lib/utils';
import type { OrderStatus } from '@/types/api';

const PAGE_SIZE = 20;
const statuses: (OrderStatus | '')[] = ['', 'Submitted', 'Processing', 'Fulfilled', 'Cancelled'];

export function OrdersPage() {
  const user = useCurrentUser();
  const navigate = useNavigate();
  const list = useListParams({ sortBy: 'createdAt', sortDirection: 'Desc' });
  const status = list.filter('status') as OrderStatus | '';

  const { data, isLoading, isFetching, error, refetch } = useOrders({
    search: list.appliedSearch,
    status: status || undefined,
    sortBy: list.sortBy,
    sortDirection: list.sortDirection,
    page: list.page,
    pageSize: PAGE_SIZE,
  });
  const sortProps = { sortBy: list.sortBy, sortDirection: list.sortDirection, onSort: list.toggleSort };

  return (
    <>
      <PageHeader
        title="Orders"
        description="Operational orders. Submitting an order reserves stock; fulfilling it ships the reserved units."
        actions={
          user.permissions.canOperate && (
            <Button asChild>
              <Link to="/orders/new">
                <Plus /> New order
              </Link>
            </Button>
          )
        }
      />
      <Card>
        <div className="flex flex-col gap-3 border-b border-zinc-100 p-4 lg:flex-row lg:items-center lg:justify-between">
          <SearchInput
            label="Search orders"
            placeholder="Customer or order number"
            value={list.search}
            onChange={list.setSearch}
          />
          <div
            role="tablist"
            aria-label="Filter by status"
            className="flex gap-1 overflow-x-auto rounded-md bg-zinc-100 p-1"
          >
            {statuses.map((s) => (
              <button
                key={s || 'all'}
                role="tab"
                aria-selected={status === s}
                onClick={() => {
                  list.setFilter('status', s);
                }}
                className={cn(
                  'rounded px-3 py-1 text-sm font-medium whitespace-nowrap transition-colors',
                  status === s ? 'bg-white text-zinc-900 shadow-xs' : 'text-zinc-600 hover:text-zinc-900',
                )}
              >
                {s || 'All'}
              </button>
            ))}
          </div>
        </div>

        {error ? (
          <ErrorState error={error} onRetry={() => void refetch()} />
        ) : (
          <>
            <Table aria-busy={isFetching}>
              <thead>
                <tr>
                  <SortableHeader field="number" label="Order" {...sortProps} />
                  <SortableHeader field="customer" label="Customer" {...sortProps} />
                  <SortableHeader field="status" label="Status" {...sortProps} />
                  <Th className="hidden text-right md:table-cell">Items</Th>
                  <SortableHeader field="total" label="Total" align="right" {...sortProps} />
                  <SortableHeader field="createdAt" label="Created" {...sortProps} />
                </tr>
              </thead>
              <tbody className={cn(isFetching && !isLoading && 'opacity-60 transition-opacity')}>
                {isLoading ? (
                  <LoadingRows columns={6} />
                ) : (
                  data?.items.map((o) => (
                    <tr
                      key={o.id}
                      className="cursor-pointer hover:bg-zinc-50"
                      onClick={() => void navigate(`/orders/${o.id}`)}
                    >
                      <Td>
                        <Link
                          to={`/orders/${o.id}`}
                          onClick={(e) => {
                            e.stopPropagation();
                          }}
                          className="font-mono text-xs font-medium text-brand-700 hover:underline"
                        >
                          {o.number}
                        </Link>
                      </Td>
                      <Td className="font-medium text-zinc-900">{o.customerName}</Td>
                      <Td>
                        <OrderStatusBadge status={o.status} />
                      </Td>
                      <Td className="tabular hidden text-right md:table-cell">
                        {formatNumber(o.totalQuantity)}
                        <span className="block text-xs text-zinc-400">
                          {o.lineCount} line{o.lineCount === 1 ? '' : 's'}
                        </span>
                      </Td>
                      <Td className="tabular text-right font-medium text-zinc-900">
                        {formatMoney(o.totalAmount, o.currency)}
                      </Td>
                      <Td className="text-xs whitespace-nowrap text-zinc-500">
                        {formatDateTime(o.createdAt)}
                        <span className="block text-zinc-400">{o.createdBy}</span>
                      </Td>
                    </tr>
                  ))
                )}
              </tbody>
            </Table>
            {data?.items.length === 0 && (
              <EmptyState
                title={list.appliedSearch || status ? 'No orders match your filters' : 'No orders yet'}
                description={
                  user.permissions.canOperate ? 'Create an order to reserve stock for a customer.' : undefined
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
    </>
  );
}

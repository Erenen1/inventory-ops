import { useState } from 'react';
import { Link } from 'react-router';
import { EmptyState, ErrorState, LoadingRows, Pagination } from '@/components/shared/page';
import { Badge, Table, Td, Th } from '@/components/ui/surface';
import { useStockMovements } from '@/features/inventory/api';
import { formatDateTime, signed } from '@/lib/format';
import { cn } from '@/lib/utils';
import type { StockMovement } from '@/types/api';

const typeLabel: Record<StockMovement['type'], string> = {
  Adjustment: 'Adjustment',
  Reservation: 'Reserved',
  ReservationRelease: 'Released',
  Fulfillment: 'Shipped',
};

function Delta({ value }: { value: number }) {
  if (value === 0) return <span className="text-zinc-300">—</span>;
  return (
    <span className={cn('tabular font-medium', value > 0 ? 'text-emerald-700' : 'text-zinc-700')}>{signed(value)}</span>
  );
}

export function MovementHistory({ productId }: { productId: string }) {
  const [page, setPage] = useState(1);
  const { data, isLoading, error, refetch, isFetching } = useStockMovements(productId, page);

  if (error) return <ErrorState error={error} onRetry={() => void refetch()} />;

  return (
    <>
      <Table>
        <thead>
          <tr>
            <Th>When</Th>
            <Th>Movement</Th>
            <Th className="text-right">On hand</Th>
            <Th className="text-right">Reserved</Th>
            <Th className="hidden md:table-cell">Reference</Th>
            <Th className="hidden lg:table-cell">By</Th>
          </tr>
        </thead>
        <tbody>
          {isLoading ? (
            <LoadingRows columns={6} rows={4} />
          ) : (
            data?.items.map((m) => (
              <tr key={m.id}>
                <Td className="text-xs whitespace-nowrap text-zinc-500">{formatDateTime(m.occurredAt)}</Td>
                <Td>
                  <Badge tone={m.type === 'Adjustment' ? 'brand' : 'neutral'}>{typeLabel[m.type]}</Badge>
                  {m.reason && <span className="ml-2 text-xs text-zinc-500">{m.reason}</span>}
                </Td>
                <Td className="text-right">
                  <Delta value={m.onHandChange} />
                  <span className="tabular block text-xs text-zinc-400">= {m.onHandAfter}</span>
                </Td>
                <Td className="text-right">
                  <Delta value={m.reservedChange} />
                  <span className="tabular block text-xs text-zinc-400">= {m.reservedAfter}</span>
                </Td>
                <Td className="hidden max-w-56 truncate text-sm md:table-cell">
                  {m.orderId ? (
                    <Link to={`/orders/${m.orderId}`} className="font-mono text-xs text-brand-700 hover:underline">
                      {m.orderNumber}
                    </Link>
                  ) : (
                    <span className="text-zinc-500" title={m.note ?? undefined}>
                      {m.note ?? '—'}
                    </span>
                  )}
                </Td>
                <Td className="hidden text-xs text-zinc-500 lg:table-cell">{m.performedBy}</Td>
              </tr>
            ))
          )}
        </tbody>
      </Table>
      {data?.items.length === 0 && <EmptyState title="No stock movements yet" />}
      {data && data.totalCount > data.pageSize && (
        <Pagination
          page={page}
          pageSize={data.pageSize}
          totalCount={data.totalCount}
          onPageChange={setPage}
          isFetching={isFetching}
        />
      )}
    </>
  );
}

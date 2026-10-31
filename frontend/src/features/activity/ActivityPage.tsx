import { AlertTriangle, Package, ShoppingCart, Warehouse } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router';
import { EmptyState, ErrorState, PageHeader, Pagination } from '@/components/shared/page';
import { Select } from '@/components/ui/form-controls';
import { Badge, Card, Skeleton } from '@/components/ui/surface';
import { useActivity } from '@/features/activity/api';
import { formatDateTime, formatRelative } from '@/lib/format';
import { cn } from '@/lib/utils';
import type { ActivityEntry } from '@/types/api';

const PAGE_SIZE = 25;

const moduleIcon: Record<ActivityEntry['module'], React.ReactNode> = {
  catalog: <Package />,
  inventory: <Warehouse />,
  orders: <ShoppingCart />,
};

function subjectLink(entry: ActivityEntry): string | null {
  if (!entry.subjectId) return null;
  if (entry.module === 'orders') return `/orders/${entry.subjectId}`;
  return `/products/${entry.subjectId}`;
}

export function ActivityPage() {
  const [module, setModule] = useState('');
  const [page, setPage] = useState(1);
  const { data, isLoading, error, refetch, isFetching } = useActivity({
    module: module || undefined,
    page,
    pageSize: PAGE_SIZE,
  });

  return (
    <>
      <PageHeader
        title="Activity"
        description="Operational events recorded asynchronously from the event stream; entries can appear a few seconds after the change."
      />
      <Card>
        <div className="flex items-center gap-3 border-b border-zinc-100 p-4">
          <Select
            aria-label="Filter by module"
            className="w-48"
            value={module}
            onChange={(e) => {
              setModule(e.target.value);
              setPage(1);
            }}
          >
            <option value="">All modules</option>
            <option value="orders">Orders</option>
            <option value="inventory">Inventory</option>
            <option value="catalog">Catalog</option>
          </Select>
        </div>

        {error ? (
          <ErrorState error={error} onRetry={() => void refetch()} />
        ) : isLoading ? (
          <div className="space-y-4 p-5">
            {Array.from({ length: 6 }, (_, i) => (
              <Skeleton key={i} className="h-10" />
            ))}
          </div>
        ) : data?.items.length === 0 ? (
          <EmptyState title="No activity yet" description="Product, stock and order changes will be listed here." />
        ) : (
          <ul className={cn('divide-y divide-zinc-100', isFetching && 'opacity-70')}>
            {data?.items.map((entry) => {
              const href = subjectLink(entry);
              return (
                <li key={entry.id} className="flex gap-3 px-5 py-3.5">
                  <span
                    className={cn(
                      'mt-0.5 flex size-8 shrink-0 items-center justify-center rounded-full [&_svg]:size-4',
                      entry.severity === 'Warning' ? 'bg-amber-50 text-amber-700' : 'bg-zinc-100 text-zinc-600',
                    )}
                    aria-hidden
                  >
                    {entry.severity === 'Warning' ? <AlertTriangle /> : moduleIcon[entry.module]}
                  </span>
                  <div className="min-w-0 flex-1">
                    <p className="text-sm text-zinc-900">
                      {href ? (
                        <Link to={href} className="hover:underline">
                          {entry.summary}
                        </Link>
                      ) : (
                        entry.summary
                      )}
                    </p>
                    <p className="mt-0.5 flex flex-wrap items-center gap-x-2 text-xs text-zinc-500">
                      <time dateTime={entry.occurredAt} title={formatDateTime(entry.occurredAt)}>
                        {formatRelative(entry.occurredAt)}
                      </time>
                      {entry.actor && <span>· {entry.actor}</span>}
                      <span className="font-mono text-zinc-400">· {entry.eventType}</span>
                    </p>
                  </div>
                  {entry.severity === 'Warning' && (
                    <Badge tone="warning" className="h-fit">
                      Warning
                    </Badge>
                  )}
                </li>
              );
            })}
          </ul>
        )}
        {data && data.totalCount > 0 && (
          <Pagination
            page={page}
            pageSize={PAGE_SIZE}
            totalCount={data.totalCount}
            onPageChange={setPage}
            isFetching={isFetching}
          />
        )}
      </Card>
    </>
  );
}

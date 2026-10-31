import { AlertTriangle, ArrowRight, Boxes, Package, ShoppingCart } from 'lucide-react';
import { Link } from 'react-router';
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { OrderStatusBadge } from '@/components/shared/badges';
import { EmptyState, ErrorState, PageHeader, StatCard } from '@/components/shared/page';
import { Card, CardBody, CardHeader, CardTitle, Skeleton } from '@/components/ui/surface';
import { useActivity } from '@/features/activity/api';
import { useCurrentUser } from '@/features/auth/session';
import { useDashboardSummary } from '@/features/dashboard/api';
import { formatMoney, formatNumber, formatRelative } from '@/lib/format';
import type { DashboardSummary, OrderStatus } from '@/types/api';

const statusBar: Record<OrderStatus, string> = {
  Submitted: 'bg-sky-500',
  Processing: 'bg-amber-500',
  Fulfilled: 'bg-emerald-500',
  Cancelled: 'bg-zinc-300',
};

export function DashboardPage() {
  const { data, isLoading, error, refetch } = useDashboardSummary();

  return (
    <>
      <PageHeader
        title="Overview"
        description={
          data ? `Operational snapshot · refreshed ${formatRelative(data.generatedAt)}` : 'Operational snapshot'
        }
      />
      {error ? (
        <Card>
          <ErrorState error={error} onRetry={() => void refetch()} />
        </Card>
      ) : isLoading || !data ? (
        <DashboardSkeleton />
      ) : (
        <DashboardContent summary={data} />
      )}
    </>
  );
}

function DashboardContent({ summary }: { summary: DashboardSummary }) {
  const user = useCurrentUser();
  const open = summary.ordersByStatus
    .filter((s) => s.status === 'Submitted' || s.status === 'Processing')
    .reduce((sum, s) => sum + s.count, 0);

  return (
    <div className="space-y-6">
      <section aria-label="Key metrics" className="grid grid-cols-2 gap-3 sm:gap-4 xl:grid-cols-4">
        <StatCard
          label="Active products"
          value={formatNumber(summary.activeProducts)}
          hint={`${formatNumber(summary.inactiveProducts)} inactive`}
          icon={<Package />}
        />
        <StatCard
          label="Orders"
          value={formatNumber(summary.totalOrders)}
          hint={`${formatNumber(open)} open (submitted or processing)`}
          icon={<ShoppingCart />}
        />
        <StatCard
          label="Low stock"
          value={formatNumber(summary.lowStockProducts)}
          hint={`${formatNumber(summary.outOfStockProducts)} out of stock`}
          icon={<AlertTriangle />}
          tone={summary.lowStockProducts > 0 ? 'warning' : 'neutral'}
        />
        <StatCard
          label="Available units"
          value={formatNumber(summary.totalAvailable)}
          hint={`${formatNumber(summary.totalOnHand)} on hand · ${formatNumber(summary.totalReserved)} reserved`}
          icon={<Boxes />}
        />
      </section>

      <div className="grid gap-6 lg:grid-cols-3">
        <Card className="lg:col-span-2">
          <CardHeader>
            <CardTitle>Orders created · last 14 days</CardTitle>
          </CardHeader>
          <CardBody className="h-64 pt-4">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart
                data={summary.ordersLast14Days.map((d) => ({ ...d, label: d.date.slice(5) }))}
                margin={{ left: -24, right: 4 }}
              >
                <CartesianGrid vertical={false} stroke="#f4f4f5" />
                <XAxis
                  dataKey="label"
                  tickLine={false}
                  axisLine={false}
                  fontSize={11}
                  stroke="#a1a1aa"
                  interval="preserveStartEnd"
                />
                <YAxis allowDecimals={false} tickLine={false} axisLine={false} fontSize={11} stroke="#a1a1aa" />
                <Tooltip
                  cursor={{ fill: '#f4f4f5' }}
                  contentStyle={{ borderRadius: 8, borderColor: '#e4e4e7', fontSize: 12 }}
                  formatter={(v) => [v, 'Orders']}
                />
                <Bar dataKey="count" fill="#0f766e" radius={[3, 3, 0, 0]} maxBarSize={28} />
              </BarChart>
            </ResponsiveContainer>
          </CardBody>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Orders by status</CardTitle>
          </CardHeader>
          <CardBody className="space-y-4">
            {summary.ordersByStatus.map((s) => (
              <div key={s.status}>
                <div className="mb-1.5 flex items-center justify-between text-sm">
                  <span className="text-zinc-600">{s.status}</span>
                  <span className="tabular font-medium">{formatNumber(s.count)}</span>
                </div>
                <div className="h-1.5 rounded-full bg-zinc-100">
                  <div
                    className={`h-1.5 rounded-full ${statusBar[s.status]}`}
                    style={{ width: `${summary.totalOrders ? (s.count / summary.totalOrders) * 100 : 0}%` }}
                  />
                </div>
              </div>
            ))}
            <div className="border-t border-zinc-100 pt-3 text-sm">
              <p className="text-zinc-500">Fulfilled order value · 30 days</p>
              <p className="tabular mt-0.5 font-medium">
                {summary.fulfilledValueLast30Days.length === 0
                  ? '—'
                  : summary.fulfilledValueLast30Days.map((v) => formatMoney(v.amount, v.currency)).join(' · ')}
              </p>
              <p className="mt-1 text-xs text-zinc-400">
                Sum of fulfilled order totals; not revenue (no payments, tax or returns).
              </p>
            </div>
          </CardBody>
        </Card>
      </div>

      <div className="grid gap-6 lg:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle>Low stock</CardTitle>
            <Link
              to="/inventory?lowStockOnly=true"
              className="inline-flex items-center gap-1 text-sm text-brand-700 hover:underline"
            >
              View all <ArrowRight className="size-3.5" />
            </Link>
          </CardHeader>
          {summary.lowStock.length === 0 ? (
            <EmptyState title="All products are above their reorder threshold" />
          ) : (
            <ul className="divide-y divide-zinc-100">
              {summary.lowStock.map((item) => (
                <li key={item.productId} className="flex items-center justify-between gap-3 px-5 py-3">
                  <Link to={`/products/${item.productId}`} className="min-w-0 hover:underline">
                    <span className="block truncate text-sm font-medium text-zinc-900">{item.productName}</span>
                    <span className="font-mono text-xs text-zinc-500">{item.sku}</span>
                  </Link>
                  <span className="tabular shrink-0 text-right text-sm">
                    <span
                      className={item.available === 0 ? 'font-semibold text-red-600' : 'font-semibold text-amber-700'}
                    >
                      {formatNumber(item.available)}
                    </span>
                    <span className="text-zinc-400"> / {formatNumber(item.reorderThreshold)} min</span>
                  </span>
                </li>
              ))}
            </ul>
          )}
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Recent orders</CardTitle>
            <Link to="/orders" className="inline-flex items-center gap-1 text-sm text-brand-700 hover:underline">
              All orders <ArrowRight className="size-3.5" />
            </Link>
          </CardHeader>
          {summary.recentOrders.length === 0 ? (
            <EmptyState title="No orders yet" />
          ) : (
            <ul className="divide-y divide-zinc-100">
              {summary.recentOrders.map((order) => (
                <li key={order.id} className="flex items-center justify-between gap-3 px-5 py-3">
                  <Link to={`/orders/${order.id}`} className="min-w-0 hover:underline">
                    <span className="block truncate text-sm font-medium text-zinc-900">{order.customerName}</span>
                    <span className="text-xs text-zinc-500">
                      <span className="font-mono">{order.number}</span> · {formatRelative(order.createdAt)}
                    </span>
                  </Link>
                  <div className="flex shrink-0 items-center gap-3">
                    <span className="tabular hidden text-sm text-zinc-700 sm:inline">
                      {formatMoney(order.totalAmount, order.currency)}
                    </span>
                    <OrderStatusBadge status={order.status} />
                  </div>
                </li>
              ))}
            </ul>
          )}
        </Card>
      </div>

      {user.permissions.canViewActivity && <RecentActivity />}
    </div>
  );
}

function RecentActivity() {
  const { data, error, refetch } = useActivity({ page: 1, pageSize: 6 });
  return (
    <Card>
      <CardHeader>
        <CardTitle>Recent activity</CardTitle>
        <Link to="/activity" className="inline-flex items-center gap-1 text-sm text-brand-700 hover:underline">
          Activity log <ArrowRight className="size-3.5" />
        </Link>
      </CardHeader>
      {error ? (
        <ErrorState error={error} onRetry={() => void refetch()} />
      ) : !data ? (
        <div className="space-y-3 p-5">
          {[0, 1, 2].map((i) => (
            <Skeleton key={i} className="h-4 w-full" />
          ))}
        </div>
      ) : data.items.length === 0 ? (
        <EmptyState
          title="No activity recorded yet"
          description="Events appear here a few seconds after they happen."
        />
      ) : (
        <ul className="divide-y divide-zinc-100">
          {data.items.map((entry) => (
            <li key={entry.id} className="flex items-start justify-between gap-4 px-5 py-3 text-sm">
              <span className={entry.severity === 'Warning' ? 'text-amber-800' : 'text-zinc-700'}>{entry.summary}</span>
              <time
                dateTime={entry.occurredAt}
                title={new Date(entry.occurredAt).toLocaleString()}
                className="shrink-0 text-xs text-zinc-400"
              >
                {formatRelative(entry.occurredAt)}
              </time>
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}

function DashboardSkeleton() {
  return (
    <div className="space-y-6" role="status" aria-label="Loading overview">
      <div className="grid grid-cols-2 gap-3 sm:gap-4 xl:grid-cols-4">
        {[0, 1, 2, 3].map((i) => (
          <Skeleton key={i} className="h-28" />
        ))}
      </div>
      <div className="grid gap-6 lg:grid-cols-3">
        <Skeleton className="h-80 lg:col-span-2" />
        <Skeleton className="h-80" />
      </div>
    </div>
  );
}

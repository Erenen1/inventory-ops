import { ArrowLeft, Ban, CheckCircle2, PlayCircle } from 'lucide-react';
import { useState } from 'react';
import { Link, useParams } from 'react-router';
import { toast } from 'sonner';
import { OrderStatusBadge } from '@/components/shared/badges';
import { ErrorState, PageHeader } from '@/components/shared/page';
import { Button } from '@/components/ui/button';
import { Textarea } from '@/components/ui/form-controls';
import { ConfirmDialog } from '@/components/ui/overlays';
import { Card, CardBody, CardHeader, CardTitle, Skeleton, Table, Td, Th } from '@/components/ui/surface';
import { useCurrentUser } from '@/features/auth/session';
import { useOrder, useOrderTransition, type OrderAction } from '@/features/orders/api';
import { errorMessage } from '@/lib/api';
import { formatDateTime, formatMoney, formatNumber } from '@/lib/format';
import type { OrderDetails } from '@/types/api';

const actionCopy: Record<OrderAction, { title: string; description: string; confirm: string; success: string }> = {
  process: {
    title: 'Start processing?',
    description: 'The order moves to Processing. Reserved stock stays reserved until it is fulfilled or cancelled.',
    confirm: 'Start processing',
    success: 'Order moved to processing',
  },
  fulfill: {
    title: 'Mark as fulfilled?',
    description:
      'Reserved units are shipped: on-hand stock decreases by the ordered quantities. This cannot be undone.',
    confirm: 'Fulfill order',
    success: 'Order fulfilled and stock updated',
  },
  cancel: {
    title: 'Cancel this order?',
    description: 'Reserved stock is released back to available stock. The order is kept for history.',
    confirm: 'Cancel order',
    success: 'Order cancelled and reservations released',
  },
};

export function OrderDetailPage() {
  const { id = '' } = useParams();
  const user = useCurrentUser();
  const { data: order, error, refetch } = useOrder(id);
  const transition = useOrderTransition(id);
  const [pending, setPending] = useState<OrderAction | null>(null);
  const [reason, setReason] = useState('');

  if (error) {
    return (
      <Card>
        <ErrorState error={error} onRetry={() => void refetch()} />
      </Card>
    );
  }

  const confirm = () => {
    if (!pending) return;
    transition.mutate(
      { action: pending, reason },
      {
        onSuccess: () => {
          toast.success(actionCopy[pending].success);
          setPending(null);
          setReason('');
        },
        onError: (e) => {
          toast.error(errorMessage(e));
          setPending(null);
        },
      },
    );
  };

  const can = (status: OrderDetails['status']) =>
    !!order && user.permissions.canOperate && order.allowedTransitions.includes(status);

  return (
    <>
      <Link to="/orders" className="mb-3 inline-flex items-center gap-1 text-sm text-zinc-500 hover:text-zinc-800">
        <ArrowLeft className="size-4" /> Orders
      </Link>
      {!order ? (
        <div className="space-y-4">
          <Skeleton className="h-12 w-72" />
          <Skeleton className="h-64" />
        </div>
      ) : (
        <>
          <PageHeader
            title={`Order ${order.number}`}
            description={
              <span className="inline-flex items-center gap-2">
                <OrderStatusBadge status={order.status} /> {order.customerName}
              </span>
            }
            actions={
              <>
                {can('Processing') && (
                  <Button
                    onClick={() => {
                      setPending('process');
                    }}
                  >
                    <PlayCircle /> Start processing
                  </Button>
                )}
                {can('Fulfilled') && (
                  <Button
                    onClick={() => {
                      setPending('fulfill');
                    }}
                  >
                    <CheckCircle2 /> Fulfill
                  </Button>
                )}
                {can('Cancelled') && (
                  <Button
                    variant="secondary"
                    onClick={() => {
                      setPending('cancel');
                    }}
                  >
                    <Ban /> Cancel order
                  </Button>
                )}
              </>
            }
          />

          <div className="grid items-start gap-6 lg:grid-cols-3">
            <Card className="lg:col-span-2">
              <CardHeader>
                <CardTitle>Lines</CardTitle>
              </CardHeader>
              <Table>
                <thead>
                  <tr>
                    <Th>Product</Th>
                    <Th className="text-right">Unit price</Th>
                    <Th className="text-right">Qty</Th>
                    <Th className="text-right">Line total</Th>
                  </tr>
                </thead>
                <tbody>
                  {order.lines.map((line) => (
                    <tr key={line.id}>
                      <Td>
                        <Link to={`/products/${line.productId}`} className="font-medium text-zinc-900 hover:underline">
                          {line.productName}
                        </Link>
                        <span className="block font-mono text-xs text-zinc-500">{line.sku}</span>
                      </Td>
                      <Td className="tabular text-right">{formatMoney(line.unitPrice, order.currency)}</Td>
                      <Td className="tabular text-right">{formatNumber(line.quantity)}</Td>
                      <Td className="tabular text-right font-medium text-zinc-900">
                        {formatMoney(line.lineTotal, order.currency)}
                      </Td>
                    </tr>
                  ))}
                </tbody>
                <tfoot>
                  <tr>
                    <td colSpan={3} className="px-4 py-3 text-right text-sm font-medium text-zinc-600">
                      Total
                    </td>
                    <td className="tabular px-4 py-3 text-right text-base font-semibold text-zinc-900">
                      {formatMoney(order.totalAmount, order.currency)}
                    </td>
                  </tr>
                </tfoot>
              </Table>
            </Card>

            <div className="space-y-6">
              <Card>
                <CardHeader>
                  <CardTitle>Details</CardTitle>
                </CardHeader>
                <CardBody>
                  <dl className="space-y-3 text-sm">
                    <Row label="Customer" value={order.customerName} />
                    <Row label="Created" value={formatDateTime(order.createdAt)} />
                    <Row label="Created by" value={order.createdBy} />
                    {order.notes && <Row label="Notes" value={order.notes} />}
                    {order.cancellationReason && <Row label="Cancellation reason" value={order.cancellationReason} />}
                  </dl>
                </CardBody>
              </Card>
              <Card>
                <CardHeader>
                  <CardTitle>History</CardTitle>
                </CardHeader>
                <CardBody>
                  <ol className="relative space-y-4 border-l border-zinc-200 pl-4">
                    {order.history.map((h, i) => (
                      <li key={i} className="relative">
                        <span
                          className="absolute top-1.5 -left-[21px] size-2.5 rounded-full border-2 border-white bg-brand-600"
                          aria-hidden
                        />
                        <p className="text-sm font-medium text-zinc-900">
                          {h.fromStatus ? `${h.fromStatus} → ${h.toStatus}` : h.toStatus}
                        </p>
                        <p className="text-xs text-zinc-500">
                          {formatDateTime(h.changedAt)} · {h.changedBy}
                        </p>
                        {h.reason && <p className="mt-0.5 text-xs text-zinc-600">“{h.reason}”</p>}
                      </li>
                    ))}
                  </ol>
                </CardBody>
              </Card>
            </div>
          </div>
        </>
      )}

      {pending && (
        <ConfirmDialog
          open
          onOpenChange={(open) => {
            if (!open) setPending(null);
          }}
          title={actionCopy[pending].title}
          description={actionCopy[pending].description}
          confirmLabel={actionCopy[pending].confirm}
          tone={pending === 'cancel' ? 'danger' : 'primary'}
          loading={transition.isPending}
          onConfirm={confirm}
        >
          {pending === 'cancel' && (
            <div className="mt-4 space-y-1.5">
              <label htmlFor="cancel-reason" className="text-sm font-medium text-zinc-800">
                Reason <span className="font-normal text-zinc-500">(optional)</span>
              </label>
              <Textarea
                id="cancel-reason"
                rows={2}
                maxLength={500}
                value={reason}
                onChange={(e) => {
                  setReason(e.target.value);
                }}
              />
            </div>
          )}
        </ConfirmDialog>
      )}
    </>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-zinc-500">{label}</dt>
      <dd className="mt-0.5 break-words text-zinc-900">{value}</dd>
    </div>
  );
}

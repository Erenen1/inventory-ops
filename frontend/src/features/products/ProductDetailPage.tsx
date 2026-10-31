import { ArrowLeft, Pencil, Power, SlidersHorizontal } from 'lucide-react';
import { useState } from 'react';
import { Link, useParams } from 'react-router';
import { toast } from 'sonner';
import { ActiveBadge, StockBadge } from '@/components/shared/badges';
import { ErrorState, PageHeader } from '@/components/shared/page';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/form-controls';
import { ConfirmDialog, Dialog, DialogTrigger } from '@/components/ui/overlays';
import { Card, CardBody, CardHeader, CardTitle, Skeleton } from '@/components/ui/surface';
import { useCurrentUser } from '@/features/auth/session';
import { AdjustStockDialog } from '@/features/inventory/AdjustStockDialog';
import { useInventoryItem, useSetReorderThreshold } from '@/features/inventory/api';
import { MovementHistory } from '@/features/inventory/MovementHistory';
import { useProduct, useSetProductActive } from '@/features/products/api';
import { ProductFormDialog } from '@/features/products/ProductFormDialog';
import { errorMessage } from '@/lib/api';
import { formatDateTime, formatMoney, formatNumber } from '@/lib/format';
import type { InventoryItem } from '@/types/api';

export function ProductDetailPage() {
  const { id = '' } = useParams();
  const user = useCurrentUser();
  const product = useProduct(id);
  const stock = useInventoryItem(id);
  const setActive = useSetProductActive(id);
  const [editOpen, setEditOpen] = useState(false);
  const [adjustOpen, setAdjustOpen] = useState(false);
  const [confirmOpen, setConfirmOpen] = useState(false);

  if (product.error) {
    return (
      <Card>
        <ErrorState error={product.error} onRetry={() => void product.refetch()} />
      </Card>
    );
  }

  const p = product.data;
  const toggleActive = () => {
    if (!p) return;
    setActive.mutate(!p.isActive, {
      onSuccess: () => {
        toast.success(p.isActive ? 'Product deactivated' : 'Product reactivated');
        setConfirmOpen(false);
      },
      onError: (error) => toast.error(errorMessage(error)),
    });
  };

  return (
    <>
      <Link to="/products" className="mb-3 inline-flex items-center gap-1 text-sm text-zinc-500 hover:text-zinc-800">
        <ArrowLeft className="size-4" /> Products
      </Link>
      {!p ? (
        <Skeleton className="mb-6 h-12 w-80" />
      ) : (
        <PageHeader
          title={p.name}
          description={
            <span className="inline-flex flex-wrap items-center gap-2">
              <span className="font-mono">{p.sku}</span>
              <ActiveBadge isActive={p.isActive} />
            </span>
          }
          actions={
            user.permissions.canOperate && (
              <>
                <Dialog open={adjustOpen} onOpenChange={setAdjustOpen}>
                  <DialogTrigger asChild>
                    <Button variant="secondary" disabled={!stock.data}>
                      <SlidersHorizontal /> Adjust stock
                    </Button>
                  </DialogTrigger>
                  {adjustOpen && stock.data && (
                    <AdjustStockDialog
                      item={stock.data}
                      onDone={() => {
                        setAdjustOpen(false);
                      }}
                    />
                  )}
                </Dialog>
                <Dialog open={editOpen} onOpenChange={setEditOpen}>
                  <DialogTrigger asChild>
                    <Button variant="secondary">
                      <Pencil /> Edit
                    </Button>
                  </DialogTrigger>
                  {editOpen && (
                    <ProductFormDialog
                      product={p}
                      onSaved={() => {
                        setEditOpen(false);
                      }}
                    />
                  )}
                </Dialog>
                <Button
                  variant={p.isActive ? 'ghost' : 'secondary'}
                  onClick={() => {
                    setConfirmOpen(true);
                  }}
                >
                  <Power /> {p.isActive ? 'Deactivate' : 'Reactivate'}
                </Button>
              </>
            )
          }
        />
      )}

      <div className="grid items-start gap-6 lg:grid-cols-3">
        <Card className="lg:col-span-2">
          <CardHeader>
            <CardTitle>Details</CardTitle>
          </CardHeader>
          <CardBody>
            {!p ? (
              <Skeleton className="h-24" />
            ) : (
              <dl className="grid gap-x-6 gap-y-4 text-sm sm:grid-cols-2">
                <Detail label="Category" value={p.categoryName} />
                <Detail
                  label="Unit price"
                  value={<span className="tabular">{formatMoney(p.unitPrice, p.currency)}</span>}
                />
                <Detail label="Created" value={formatDateTime(p.createdAt)} />
                <Detail label="Last updated" value={formatDateTime(p.updatedAt)} />
                <div className="sm:col-span-2">
                  <dt className="text-zinc-500">Description</dt>
                  <dd className="mt-1 whitespace-pre-line text-zinc-800">
                    {p.description ?? <span className="text-zinc-400">No description</span>}
                  </dd>
                </div>
              </dl>
            )}
          </CardBody>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Stock</CardTitle>
            {stock.data && <StockBadge available={stock.data.available} isLowStock={stock.data.isLowStock} />}
          </CardHeader>
          <CardBody>
            {stock.error ? (
              <p className="text-sm text-red-600">{errorMessage(stock.error)}</p>
            ) : !stock.data ? (
              <Skeleton className="h-24" />
            ) : (
              <StockSummary item={stock.data} canEdit={user.permissions.canOperate} />
            )}
          </CardBody>
        </Card>
      </div>

      <Card className="mt-6">
        <CardHeader>
          <CardTitle>Stock movements</CardTitle>
        </CardHeader>
        <MovementHistory productId={id} />
      </Card>

      {p && (
        <ConfirmDialog
          open={confirmOpen}
          onOpenChange={setConfirmOpen}
          title={p.isActive ? `Deactivate ${p.sku}?` : `Reactivate ${p.sku}?`}
          description={
            p.isActive
              ? 'The product can no longer be added to new orders. Existing orders, stock and history are kept.'
              : 'The product becomes available for new orders again.'
          }
          confirmLabel={p.isActive ? 'Deactivate product' : 'Reactivate product'}
          tone={p.isActive ? 'danger' : 'primary'}
          loading={setActive.isPending}
          onConfirm={toggleActive}
        />
      )}
    </>
  );
}

function Detail({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div>
      <dt className="text-zinc-500">{label}</dt>
      <dd className="mt-1 text-zinc-900">{value}</dd>
    </div>
  );
}

function StockSummary({ item, canEdit }: { item: InventoryItem; canEdit: boolean }) {
  const setThreshold = useSetReorderThreshold(item.productId);
  const [threshold, setThresholdValue] = useState(String(item.reorderThreshold));
  const parsed = Number(threshold);
  const valid = Number.isInteger(parsed) && parsed >= 0 && parsed <= 1_000_000;

  return (
    <div className="space-y-4">
      <dl className="grid grid-cols-3 gap-3 text-center">
        {[
          ['On hand', item.onHand],
          ['Reserved', item.reserved],
          ['Available', item.available],
        ].map(([label, value]) => (
          <div key={label} className="rounded-md bg-zinc-50 py-3">
            <dt className="text-xs text-zinc-500">{label}</dt>
            <dd className="tabular mt-1 text-lg font-semibold">{formatNumber(value as number)}</dd>
          </div>
        ))}
      </dl>
      <form
        className="flex items-end gap-2"
        onSubmit={(e) => {
          e.preventDefault();
          if (!valid) return;
          setThreshold.mutate(parsed, {
            onSuccess: () => toast.success('Reorder threshold updated'),
            onError: (error) => toast.error(errorMessage(error)),
          });
        }}
      >
        <div className="flex-1 space-y-1.5">
          <label htmlFor="threshold" className="text-sm font-medium text-zinc-800">
            Reorder threshold
          </label>
          <Input
            id="threshold"
            type="number"
            min="0"
            step="1"
            value={threshold}
            disabled={!canEdit}
            aria-invalid={!valid}
            onChange={(e) => {
              setThresholdValue(e.target.value);
            }}
          />
        </div>
        {canEdit && (
          <Button
            type="submit"
            variant="secondary"
            disabled={!valid || parsed === item.reorderThreshold}
            loading={setThreshold.isPending}
          >
            Save
          </Button>
        )}
      </form>
    </div>
  );
}

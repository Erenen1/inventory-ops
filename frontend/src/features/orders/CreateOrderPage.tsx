import { zodResolver } from '@hookform/resolvers/zod';
import { ArrowLeft, Plus, Trash2 } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useFieldArray, useForm, useWatch } from 'react-hook-form';
import { Link, useNavigate } from 'react-router';
import { toast } from 'sonner';
import { ErrorState, FormField, PageHeader } from '@/components/shared/page';
import { Button } from '@/components/ui/button';
import { Input, Select, Textarea } from '@/components/ui/form-controls';
import { Card, CardBody, CardHeader, CardTitle, Skeleton } from '@/components/ui/surface';
import { useSubmitOrder } from '@/features/orders/api';
import { orderSchema, type OrderFormValues } from '@/features/orders/schema';
import { useProducts } from '@/features/products/api';
import { ApiError, errorMessage } from '@/lib/api';
import { formatMoney, formatNumber } from '@/lib/format';
import { newIdempotencyKey } from '@/lib/ids';
import type { ProductListItem } from '@/types/api';

export function CreateOrderPage() {
  const navigate = useNavigate();
  // Active products with live availability. 100 is the API's page-size limit; see README limitations.
  const products = useProducts({ isActive: true, sortBy: 'name', sortDirection: 'Asc', page: 1, pageSize: 100 });
  const [idempotencyKey] = useState(newIdempotencyKey);
  const submit = useSubmitOrder(idempotencyKey);
  const [formError, setFormError] = useState<string | null>(null);

  const form = useForm<OrderFormValues>({
    resolver: zodResolver(orderSchema),
    defaultValues: { customerName: '', notes: '', lines: [{ productId: '', quantity: 1 }] },
  });
  const { fields, append, remove } = useFieldArray({ control: form.control, name: 'lines' });
  const lines = useWatch({ control: form.control, name: 'lines' });
  const { errors } = form.formState;

  const byId = useMemo(() => new Map((products.data?.items ?? []).map((p) => [p.id, p])), [products.data]);
  const selected = lines.map((l) => byId.get(l.productId));
  const currencies = new Set(selected.filter((p): p is ProductListItem => !!p).map((p) => p.currency));
  const currency = currencies.size === 1 ? [...currencies][0] : undefined;
  const total = lines.reduce(
    (sum, line, i) => sum + (selected[i]?.unitPrice ?? 0) * (Number.isFinite(line.quantity) ? line.quantity : 0),
    0,
  );

  const onSubmit = form.handleSubmit(async (values) => {
    setFormError(null);
    try {
      const order = await submit.mutateAsync({
        customerName: values.customerName.trim(),
        notes: values.notes.trim() || null,
        lines: values.lines,
      });
      toast.success(`Order ${order.number} submitted and stock reserved`);
      void navigate(`/orders/${order.id}`, { replace: true });
    } catch (error) {
      setFormError(
        error instanceof ApiError && error.code === 'inventory.insufficient_stock'
          ? `${error.message} Stock may have changed since the page loaded; availability has been refreshed.`
          : errorMessage(error),
      );
      if (error instanceof ApiError && error.status === 409) void products.refetch();
    }
  });

  return (
    <>
      <Link to="/orders" className="mb-3 inline-flex items-center gap-1 text-sm text-zinc-500 hover:text-zinc-800">
        <ArrowLeft className="size-4" /> Orders
      </Link>
      <PageHeader title="New order" description="Stock is reserved when the order is submitted." />

      {products.error ? (
        <Card>
          <ErrorState error={products.error} onRetry={() => void products.refetch()} />
        </Card>
      ) : (
        <form onSubmit={onSubmit} noValidate className="grid gap-6 lg:grid-cols-3">
          <div className="space-y-6 lg:col-span-2">
            <Card>
              <CardHeader>
                <CardTitle>Customer</CardTitle>
              </CardHeader>
              <CardBody className="space-y-4">
                <FormField id="customerName" label="Customer name" error={errors.customerName?.message}>
                  <Input
                    id="customerName"
                    autoComplete="organization"
                    aria-invalid={!!errors.customerName}
                    {...form.register('customerName')}
                  />
                </FormField>
                <FormField
                  id="notes"
                  label="Notes"
                  error={errors.notes?.message}
                  hint="Optional, visible on the order."
                >
                  <Textarea id="notes" rows={2} {...form.register('notes')} />
                </FormField>
              </CardBody>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle>Products</CardTitle>
                <Button
                  type="button"
                  variant="secondary"
                  size="sm"
                  onClick={() => {
                    append({ productId: '', quantity: 1 });
                  }}
                  disabled={fields.length >= 50}
                >
                  <Plus /> Add line
                </Button>
              </CardHeader>
              <CardBody className="space-y-4">
                {products.isLoading ? (
                  <Skeleton className="h-16" />
                ) : (
                  fields.map((field, index) => {
                    const product = selected[index];
                    const quantity = lines[index]?.quantity ?? 0;
                    const exceeds = !!product && Number.isFinite(quantity) && quantity > product.available;
                    const lineErrors = errors.lines?.[index];
                    const takenElsewhere = new Set(lines.filter((_, i) => i !== index).map((l) => l.productId));
                    return (
                      <div
                        key={field.id}
                        className="grid gap-3 rounded-md border border-zinc-200 p-3 sm:grid-cols-[1fr_7rem_auto] sm:items-start"
                        data-testid="order-line"
                      >
                        <FormField
                          id={`lines.${index}.productId`}
                          label={`Product ${index + 1}`}
                          error={lineErrors?.productId?.message}
                        >
                          <Select
                            id={`lines.${index}.productId`}
                            aria-invalid={!!lineErrors?.productId}
                            {...form.register(`lines.${index}.productId`)}
                          >
                            <option value="">Select a product…</option>
                            {products.data?.items.map((p) => (
                              <option key={p.id} value={p.id} disabled={takenElsewhere.has(p.id) || p.available === 0}>
                                {p.sku} — {p.name} ({formatNumber(p.available)} available)
                              </option>
                            ))}
                          </Select>
                        </FormField>
                        <FormField
                          id={`lines.${index}.quantity`}
                          label="Quantity"
                          error={lineErrors?.quantity?.message}
                        >
                          <Input
                            id={`lines.${index}.quantity`}
                            type="number"
                            min="1"
                            step="1"
                            inputMode="numeric"
                            aria-invalid={!!lineErrors?.quantity || exceeds}
                            {...form.register(`lines.${index}.quantity`, { valueAsNumber: true })}
                          />
                        </FormField>
                        <div className="flex items-end justify-between gap-3 sm:h-full sm:flex-col sm:items-end sm:pt-7">
                          <Button
                            type="button"
                            variant="ghost"
                            size="icon"
                            onClick={() => {
                              remove(index);
                            }}
                            disabled={fields.length === 1}
                            aria-label={`Remove line ${index + 1}`}
                          >
                            <Trash2 />
                          </Button>
                        </div>
                        {product && (
                          <p
                            className={
                              exceeds ? 'text-xs text-amber-700 sm:col-span-3' : 'text-xs text-zinc-500 sm:col-span-3'
                            }
                            role={exceeds ? 'alert' : undefined}
                          >
                            {exceeds
                              ? `Only ${formatNumber(product.available)} available — the order will be rejected unless stock arrives first.`
                              : `${formatMoney(product.unitPrice, product.currency)} each · ${formatNumber(product.available)} available`}
                          </p>
                        )}
                      </div>
                    );
                  })
                )}
                {errors.lines?.root?.message && <p className="text-xs text-red-600">{errors.lines.root.message}</p>}
                {errors.lines?.message && <p className="text-xs text-red-600">{errors.lines.message}</p>}
              </CardBody>
            </Card>
          </div>

          <div className="lg:sticky lg:top-6 lg:self-start">
            <Card>
              <CardHeader>
                <CardTitle>Summary</CardTitle>
              </CardHeader>
              <CardBody className="space-y-3 text-sm">
                <div className="flex justify-between">
                  <span className="text-zinc-500">Lines</span>
                  <span className="tabular">{lines.filter((l) => l.productId).length}</span>
                </div>
                <div className="flex justify-between">
                  <span className="text-zinc-500">Units</span>
                  <span className="tabular">
                    {formatNumber(lines.reduce((s, l) => s + (Number.isFinite(l.quantity) ? l.quantity : 0), 0))}
                  </span>
                </div>
                <div className="flex justify-between border-t border-zinc-100 pt-3 text-base font-semibold">
                  <span>Estimated total</span>
                  <span className="tabular">{currency ? formatMoney(total, currency) : '—'}</span>
                </div>
                {currencies.size > 1 && (
                  <p className="text-xs text-amber-700">All products in an order must use the same currency.</p>
                )}
                <p className="text-xs text-zinc-500">
                  Prices are captured from the catalog when the order is submitted.
                </p>
                {formError && (
                  <p role="alert" className="rounded-md border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
                    {formError}
                  </p>
                )}
                <Button type="submit" className="w-full" loading={submit.isPending}>
                  Submit order
                </Button>
              </CardBody>
            </Card>
          </div>
        </form>
      )}
    </>
  );
}

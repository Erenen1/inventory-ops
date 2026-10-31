import { zodResolver } from '@hookform/resolvers/zod';
import { useState } from 'react';
import { useForm, useWatch } from 'react-hook-form';
import { toast } from 'sonner';
import { z } from 'zod';
import { FormField } from '@/components/shared/page';
import { Button } from '@/components/ui/button';
import { Input, Select, Textarea } from '@/components/ui/form-controls';
import { DialogClose, DialogContent, DialogFooter } from '@/components/ui/overlays';
import { adjustmentReasons, useAdjustStock } from '@/features/inventory/api';
import { applyServerErrors } from '@/lib/forms';
import { formatNumber } from '@/lib/format';
import { cn } from '@/lib/utils';
import type { InventoryItem } from '@/types/api';

const reasons = ['Received', 'CountCorrection', 'Damaged', 'Lost', 'Returned', 'Other'] as const;

const schema = z
  .object({
    direction: z.enum(['in', 'out']),
    quantity: z
      .number({ error: 'Enter a quantity.' })
      .int('Use a whole number.')
      .min(1, 'Must be at least 1.')
      .max(1_000_000),
    reason: z.enum(reasons),
    note: z.string().max(500, 'Note must be at most 500 characters.'),
  })
  .refine((v) => v.reason !== 'Other' || v.note.trim().length > 0, {
    path: ['note'],
    message: 'A note is required for "Other".',
  });
type Values = z.infer<typeof schema>;

export function AdjustStockDialog({ item, onDone }: { item: InventoryItem; onDone: () => void }) {
  const adjust = useAdjustStock(item.productId);
  const [formError, setFormError] = useState<string | null>(null);
  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { direction: 'in', quantity: Number.NaN, reason: 'Received', note: '' },
  });
  const { errors } = form.formState;
  const [direction, quantity] = useWatch({ control: form.control, name: ['direction', 'quantity'] });

  const change = Number.isFinite(quantity) ? (direction === 'in' ? quantity : -quantity) : 0;
  const projectedOnHand = item.onHand + change;
  // Preview only: the server enforces on-hand >= reserved and rejects anything else.
  const blocked = projectedOnHand < item.reserved;

  const onSubmit = form.handleSubmit(async (values) => {
    setFormError(null);
    try {
      await adjust.mutateAsync({
        quantityChange: values.direction === 'in' ? values.quantity : -values.quantity,
        reason: values.reason,
        note: values.note.trim() || null,
      });
      toast.success(`Stock for ${item.sku} adjusted`);
      onDone();
    } catch (error) {
      setFormError(applyServerErrors(error, form.setError, ['quantity', 'reason', 'note']));
    }
  });

  return (
    <DialogContent title="Adjust stock" description={`${item.productName} · ${item.sku}`}>
      <form onSubmit={onSubmit} noValidate className="space-y-4">
        {formError && (
          <p role="alert" className="rounded-md border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
            {formError}
          </p>
        )}
        <fieldset>
          <legend className="text-sm font-medium text-zinc-800">Direction</legend>
          <div className="mt-1.5 grid grid-cols-2 gap-2">
            {(['in', 'out'] as const).map((value) => (
              <label
                key={value}
                className={cn(
                  'flex cursor-pointer items-center justify-center rounded-md border px-3 py-2 text-sm font-medium has-[:focus-visible]:ring-2 has-[:focus-visible]:ring-brand-600/30',
                  direction === value
                    ? 'border-brand-600 bg-brand-50 text-brand-800'
                    : 'border-zinc-300 text-zinc-700 hover:bg-zinc-50',
                )}
              >
                <input type="radio" value={value} className="sr-only" {...form.register('direction')} />
                {value === 'in' ? 'Add stock' : 'Remove stock'}
              </label>
            ))}
          </div>
        </fieldset>
        <div className="grid gap-4 sm:grid-cols-2">
          <FormField id="quantity" label="Quantity" error={errors.quantity?.message}>
            <Input
              id="quantity"
              type="number"
              min="1"
              step="1"
              inputMode="numeric"
              aria-invalid={!!errors.quantity}
              {...form.register('quantity', { valueAsNumber: true })}
            />
          </FormField>
          <FormField id="reason" label="Reason" error={errors.reason?.message}>
            <Select id="reason" {...form.register('reason')}>
              {adjustmentReasons.map((r) => (
                <option key={r.value} value={r.value}>
                  {r.label}
                </option>
              ))}
            </Select>
          </FormField>
        </div>
        <FormField
          id="note"
          label="Note"
          error={errors.note?.message}
          hint="Reference such as a PO number or count sheet."
        >
          <Textarea id="note" rows={2} aria-invalid={!!errors.note} {...form.register('note')} />
        </FormField>

        <dl className="grid grid-cols-3 gap-2 rounded-md bg-zinc-50 p-3 text-center text-sm">
          <div>
            <dt className="text-xs text-zinc-500">On hand</dt>
            <dd className="tabular font-medium">
              {formatNumber(item.onHand)} →{' '}
              <span className={cn(blocked && 'text-red-600')}>{formatNumber(projectedOnHand)}</span>
            </dd>
          </div>
          <div>
            <dt className="text-xs text-zinc-500">Reserved</dt>
            <dd className="tabular font-medium">{formatNumber(item.reserved)}</dd>
          </div>
          <div>
            <dt className="text-xs text-zinc-500">Available</dt>
            <dd className="tabular font-medium">{formatNumber(Math.max(0, projectedOnHand - item.reserved))}</dd>
          </div>
        </dl>
        {blocked && (
          <p className="text-xs text-red-600" role="alert">
            On-hand stock cannot drop below the {formatNumber(item.reserved)} units reserved by open orders.
          </p>
        )}

        <DialogFooter>
          <DialogClose asChild>
            <Button type="button" variant="secondary">
              Cancel
            </Button>
          </DialogClose>
          <Button type="submit" loading={form.formState.isSubmitting} disabled={blocked}>
            Save adjustment
          </Button>
        </DialogFooter>
      </form>
    </DialogContent>
  );
}

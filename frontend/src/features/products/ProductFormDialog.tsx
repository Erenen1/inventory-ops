import { zodResolver } from '@hookform/resolvers/zod';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { toast } from 'sonner';
import { FormField } from '@/components/shared/page';
import { Button } from '@/components/ui/button';
import { Input, Select, Textarea } from '@/components/ui/form-controls';
import { DialogClose, DialogContent, DialogFooter } from '@/components/ui/overlays';
import { useCategories, useCreateProduct, useUpdateProduct } from '@/features/products/api';
import { currencies, productSchema, type ProductFormValues } from '@/features/products/schema';
import { applyServerErrors } from '@/lib/forms';
import type { ProductDetails } from '@/types/api';

const fields = ['sku', 'name', 'description', 'categoryId', 'unitPrice', 'currency', 'reorderThreshold'] as const;

interface ProductFormDialogProps {
  product?: ProductDetails;
  onSaved: (id: string) => void;
}

/** Create or edit form. SKU and initial reorder threshold are only set on creation. */
export function ProductFormDialog({ product, onSaved }: ProductFormDialogProps) {
  const isEdit = !!product;
  const categories = useCategories();
  const create = useCreateProduct();
  const update = useUpdateProduct(product?.id ?? '');
  const [formError, setFormError] = useState<string | null>(null);

  const form = useForm<ProductFormValues>({
    resolver: zodResolver(productSchema),
    defaultValues: {
      sku: product?.sku ?? '',
      name: product?.name ?? '',
      description: product?.description ?? '',
      categoryId: product?.categoryId ?? '',
      unitPrice: product?.unitPrice ?? Number.NaN,
      currency: (product?.currency as ProductFormValues['currency'] | undefined) ?? 'USD',
      reorderThreshold: 10,
    },
  });
  const { errors } = form.formState;

  const onSubmit = form.handleSubmit(async (values) => {
    setFormError(null);
    const details = {
      name: values.name.trim(),
      description: values.description.trim() || null,
      categoryId: values.categoryId,
      unitPrice: values.unitPrice,
      currency: values.currency,
    };
    try {
      if (isEdit) {
        await update.mutateAsync(details);
        toast.success('Product updated');
        onSaved(product.id);
      } else {
        const { id } = await create.mutateAsync({
          ...details,
          sku: values.sku.trim(),
          reorderThreshold: values.reorderThreshold,
        });
        toast.success('Product created');
        onSaved(id);
      }
    } catch (error) {
      setFormError(applyServerErrors(error, form.setError, fields));
    }
  });

  const invalid = (name: keyof ProductFormValues) => ({
    'aria-invalid': !!errors[name],
    'aria-describedby': errors[name] ? `${name}-error` : undefined,
  });

  return (
    <DialogContent
      title={isEdit ? 'Edit product' : 'New product'}
      description={isEdit ? product.sku : 'Stock starts at zero; receive stock from the inventory screen.'}
    >
      <form onSubmit={onSubmit} noValidate className="space-y-4">
        {formError && (
          <p role="alert" className="rounded-md border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
            {formError}
          </p>
        )}
        <div className="grid gap-4 sm:grid-cols-2">
          <FormField
            id="sku"
            label="SKU"
            error={errors.sku?.message}
            hint={isEdit ? 'SKU cannot be changed.' : undefined}
          >
            <Input
              id="sku"
              disabled={isEdit}
              className="font-mono uppercase"
              {...invalid('sku')}
              {...form.register('sku')}
            />
          </FormField>
          <FormField id="categoryId" label="Category" error={errors.categoryId?.message}>
            <Select id="categoryId" {...invalid('categoryId')} {...form.register('categoryId')}>
              <option value="">Select…</option>
              {categories.data?.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </Select>
          </FormField>
        </div>
        <FormField id="name" label="Name" error={errors.name?.message}>
          <Input id="name" {...invalid('name')} {...form.register('name')} />
        </FormField>
        <FormField id="description" label="Description" error={errors.description?.message} hint="Optional">
          <Textarea id="description" rows={3} {...invalid('description')} {...form.register('description')} />
        </FormField>
        <div className="grid gap-4 sm:grid-cols-3">
          <FormField id="unitPrice" label="Unit price" error={errors.unitPrice?.message} className="sm:col-span-2">
            <Input
              id="unitPrice"
              type="number"
              inputMode="decimal"
              step="0.01"
              min="0"
              {...invalid('unitPrice')}
              {...form.register('unitPrice', { valueAsNumber: true })}
            />
          </FormField>
          <FormField id="currency" label="Currency" error={errors.currency?.message}>
            <Select id="currency" {...form.register('currency')}>
              {currencies.map((c) => (
                <option key={c}>{c}</option>
              ))}
            </Select>
          </FormField>
        </div>
        {!isEdit && (
          <FormField
            id="reorderThreshold"
            label="Reorder threshold"
            error={errors.reorderThreshold?.message}
            hint="Flagged as low stock when available units fall to this level."
          >
            <Input
              id="reorderThreshold"
              type="number"
              min="0"
              step="1"
              {...invalid('reorderThreshold')}
              {...form.register('reorderThreshold', { valueAsNumber: true })}
            />
          </FormField>
        )}
        <DialogFooter>
          <DialogClose asChild>
            <Button type="button" variant="secondary">
              Cancel
            </Button>
          </DialogClose>
          <Button type="submit" loading={form.formState.isSubmitting}>
            {isEdit ? 'Save changes' : 'Create product'}
          </Button>
        </DialogFooter>
      </form>
    </DialogContent>
  );
}

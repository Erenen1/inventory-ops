import { z } from 'zod';

export const currencies = ['USD', 'EUR', 'GBP', 'TRY'] as const;

// Mirrors the backend rules for fast feedback; the API remains the authority.
export const productSchema = z.object({
  sku: z
    .string()
    .trim()
    .regex(
      /^[A-Za-z0-9][A-Za-z0-9-]{1,30}[A-Za-z0-9]$/,
      '3–32 letters, digits or dashes; must start and end with a letter or digit.',
    ),
  name: z.string().trim().min(1, 'Name is required.').max(200, 'Name must be at most 200 characters.'),
  description: z.string().max(2000, 'Description must be at most 2000 characters.'),
  categoryId: z.string().min(1, 'Choose a category.'),
  unitPrice: z
    .number({ error: 'Enter a price.' })
    .min(0, 'Price cannot be negative.')
    .max(9_999_999_999.99, 'Price is too large.')
    .refine(
      (v) => Number.isInteger(Math.round(v * 100)) && Math.abs(v * 100 - Math.round(v * 100)) < 1e-9,
      'Use at most two decimals.',
    ),
  currency: z.enum(currencies),
  reorderThreshold: z
    .number({ error: 'Enter a number.' })
    .int('Use a whole number.')
    .min(0, 'Cannot be negative.')
    .max(1_000_000),
});
export type ProductFormValues = z.infer<typeof productSchema>;

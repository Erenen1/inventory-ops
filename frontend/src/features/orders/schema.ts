import { z } from 'zod';

/** Client-side mirror of the order rules for immediate feedback; the API is authoritative. */
export const orderSchema = z.object({
  customerName: z.string().trim().min(1, 'Customer name is required.').max(200),
  notes: z.string().max(1000, 'Notes must be at most 1000 characters.'),
  lines: z
    .array(
      z.object({
        productId: z.string().min(1, 'Choose a product.'),
        quantity: z
          .number({ error: 'Enter a quantity.' })
          .int('Use a whole number.')
          .min(1, 'At least 1.')
          .max(10_000, 'At most 10,000.'),
      }),
    )
    .min(1, 'Add at least one product.')
    .max(50, 'An order can have at most 50 lines.')
    .superRefine((lines, ctx) => {
      const seen = new Set<string>();
      lines.forEach((line, index) => {
        if (line.productId && seen.has(line.productId)) {
          ctx.addIssue({
            code: 'custom',
            path: [index, 'productId'],
            message: 'This product is already on the order.',
          });
        }
        seen.add(line.productId);
      });
    }),
});
export type OrderFormValues = z.infer<typeof orderSchema>;

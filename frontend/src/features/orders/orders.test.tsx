import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { CreateOrderPage } from '@/features/orders/CreateOrderPage';
import { orderSchema } from '@/features/orders/schema';
import { renderWithProviders, stubFetch } from '@/test/render';
import type { ProductListItem } from '@/types/api';

const scanner: ProductListItem = {
  id: 'p1',
  sku: 'ELC-SCAN',
  name: 'Barcode Scanner',
  categoryId: 'c1',
  categoryName: 'Electronics',
  unitPrice: 25.5,
  currency: 'USD',
  isActive: true,
  onHand: 5,
  reserved: 2,
  available: 3,
  updatedAt: new Date().toISOString(),
};

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('order schema', () => {
  it('rejects the same product on two lines', () => {
    const result = orderSchema.safeParse({
      customerName: 'Contoso',
      notes: '',
      lines: [
        { productId: 'p1', quantity: 1 },
        { productId: 'p1', quantity: 2 },
      ],
    });
    expect(result.success).toBe(false);
    expect(result.error?.issues[0]?.message).toBe('This product is already on the order.');
  });

  it('requires at least one line and positive whole quantities', () => {
    expect(orderSchema.safeParse({ customerName: 'x', notes: '', lines: [] }).success).toBe(false);
    expect(
      orderSchema.safeParse({ customerName: 'x', notes: '', lines: [{ productId: 'p1', quantity: 0 }] }).success,
    ).toBe(false);
    expect(
      orderSchema.safeParse({ customerName: 'x', notes: '', lines: [{ productId: 'p1', quantity: 1.5 }] }).success,
    ).toBe(false);
  });
});

describe('CreateOrderPage', () => {
  async function fillOrder(quantity: string) {
    await userEvent.type(screen.getByLabelText('Customer name'), 'Contoso');
    await waitFor(() => {
      expect(screen.getByRole('option', { name: /ELC-SCAN/ })).toBeInTheDocument();
    });
    await userEvent.selectOptions(screen.getByLabelText('Product 1'), 'p1');
    const qty = screen.getByLabelText('Quantity');
    await userEvent.clear(qty);
    await userEvent.type(qty, quantity);
  }

  it('warns when the quantity exceeds available stock and previews the total', async () => {
    stubFetch({
      'GET /api/v1/products': { body: { items: [scanner], page: 1, pageSize: 100, totalCount: 1, totalPages: 1 } },
    });
    renderWithProviders(<CreateOrderPage />, { role: 'Manager' });

    await fillOrder('4');

    expect(screen.getByText(/Only 3 available/)).toBeInTheDocument();
    expect(screen.getByText('$102.00')).toBeInTheDocument();
  });

  it('sends an idempotency key and surfaces stock conflicts from the server', async () => {
    const calls = stubFetch({
      'GET /api/v1/products': { body: { items: [scanner], page: 1, pageSize: 100, totalCount: 1, totalPages: 1 } },
      'POST /api/v1/orders': {
        status: 409,
        body: {
          title: 'Insufficient stock',
          detail: 'Insufficient stock: requested 2, available 1.',
          code: 'inventory.insufficient_stock',
        },
      },
    });
    renderWithProviders(<CreateOrderPage />, { role: 'Manager' });

    await fillOrder('2');
    await userEvent.click(screen.getByRole('button', { name: 'Submit order' }));
    expect(await screen.findByText(/requested 2, available 1/)).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Submit order' }));
    await waitFor(() => {
      expect(calls.filter((c) => c.method === 'POST')).toHaveLength(2);
    });
    const [first, second] = calls.filter((c) => c.method === 'POST');
    expect(first?.headers['Idempotency-Key']).toBeTruthy();
    // Retrying the same form reuses the key, so a duplicate submit can never create two orders.
    expect(second?.headers['Idempotency-Key']).toBe(first?.headers['Idempotency-Key']);
    expect(first?.body).toEqual({ customerName: 'Contoso', notes: null, lines: [{ productId: 'p1', quantity: 2 }] });
  });
});

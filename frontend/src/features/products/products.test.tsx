import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Dialog } from '@/components/ui/overlays';
import { ProductFormDialog } from '@/features/products/ProductFormDialog';
import { ProductsPage } from '@/features/products/ProductsPage';
import { productSchema } from '@/features/products/schema';
import { renderWithProviders, stubFetch } from '@/test/render';
import type { PagedResult, ProductListItem } from '@/types/api';

const product: ProductListItem = {
  id: 'p1',
  sku: 'NET-SW-24G',
  name: '24-Port Gigabit Switch',
  categoryId: 'c1',
  categoryName: 'Networking',
  unitPrice: 219,
  currency: 'USD',
  isActive: true,
  onHand: 14,
  reserved: 4,
  available: 10,
  updatedAt: new Date().toISOString(),
};
const page = (items: ProductListItem[]): PagedResult<ProductListItem> => ({
  items,
  page: 1,
  pageSize: 20,
  totalCount: items.length,
  totalPages: 1,
});
const categories = { body: [{ id: 'c1', name: 'Networking', productCount: 1 }] };

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('product schema', () => {
  const valid = {
    sku: 'ABC-1',
    name: 'Widget',
    description: '',
    categoryId: 'c1',
    unitPrice: 9.99,
    currency: 'USD',
    reorderThreshold: 5,
  } as const;

  it('accepts a valid product', () => {
    expect(productSchema.safeParse(valid).success).toBe(true);
  });

  it.each([
    ['negative price', { unitPrice: -1 }],
    ['sub-cent price', { unitPrice: 1.005 }],
    ['malformed SKU', { sku: '-bad' }],
    ['missing category', { categoryId: '' }],
    ['fractional threshold', { reorderThreshold: 1.5 }],
  ])('rejects %s', (_, change) => {
    expect(productSchema.safeParse({ ...valid, ...change }).success).toBe(false);
  });
});

describe('ProductsPage', () => {
  it('lists products with availability and reserved stock', async () => {
    stubFetch({ 'GET /api/v1/products': { body: page([product]) }, 'GET /api/v1/categories': categories });
    renderWithProviders(<ProductsPage />, { role: 'Viewer', route: '/products' });

    const row = (await screen.findByText('24-Port Gigabit Switch')).closest('tr');
    expect(row).not.toBeNull();
    expect(within(row as HTMLElement).getByText('$219.00')).toBeInTheDocument();
    expect(within(row as HTMLElement).getByText('4 reserved')).toBeInTheDocument();
  });

  it('hides write actions from viewers and shows them to managers', async () => {
    stubFetch({ 'GET /api/v1/products': { body: page([product]) }, 'GET /api/v1/categories': categories });
    const { unmount } = renderWithProviders(<ProductsPage />, { role: 'Viewer' });
    await screen.findByText('24-Port Gigabit Switch');
    expect(screen.queryByRole('button', { name: /new product/i })).not.toBeInTheDocument();
    unmount();

    renderWithProviders(<ProductsPage />, { role: 'Manager' });
    expect(await screen.findByRole('button', { name: /new product/i })).toBeInTheDocument();
  });

  it('shows a loading skeleton, then an empty state', async () => {
    stubFetch({ 'GET /api/v1/products': { body: page([]) }, 'GET /api/v1/categories': categories });
    renderWithProviders(<ProductsPage />);

    expect(screen.getByRole('table')).toHaveAttribute('aria-busy', 'true');
    expect(await screen.findByText('No products yet')).toBeInTheDocument();
  });

  it('shows a server error with a retry action', async () => {
    stubFetch({
      'GET /api/v1/products': { status: 500, body: { title: 'Unexpected error', correlationId: 'abc123' } },
      'GET /api/v1/categories': categories,
    });
    renderWithProviders(<ProductsPage />);

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Reference: abc123');
    expect(within(alert).getByRole('button', { name: /try again/i })).toBeInTheDocument();
  });
});

describe('ProductFormDialog', () => {
  it('validates input before calling the API', async () => {
    const calls = stubFetch({ 'GET /api/v1/categories': categories });
    renderWithProviders(
      <Dialog open>
        <ProductFormDialog onSaved={vi.fn()} />
      </Dialog>,
    );

    await userEvent.click(await screen.findByRole('button', { name: 'Create product' }));

    expect(await screen.findByText(/3–32 letters/)).toBeInTheDocument();
    expect(screen.getByText('Name is required.')).toBeInTheDocument();
    expect(screen.getByText('Enter a price.')).toBeInTheDocument();
    expect(calls.some((c) => c.method === 'POST')).toBe(false);
  });

  it('maps server-side conflicts back onto the form', async () => {
    document.cookie = 'XSRF-TOKEN=token-123';
    const calls = stubFetch({
      'GET /api/v1/categories': categories,
      'POST /api/v1/products': {
        status: 409,
        body: {
          title: 'Conflict',
          detail: "A product with SKU 'ABC-1' already exists.",
          code: 'catalog.duplicate_sku',
        },
      },
    });
    const onSaved = vi.fn();
    renderWithProviders(
      <Dialog open>
        <ProductFormDialog onSaved={onSaved} />
      </Dialog>,
    );
    const dialog = await screen.findByRole('dialog');

    await userEvent.type(within(dialog).getByLabelText('SKU'), 'ABC-1');
    await waitFor(() => {
      expect(within(dialog).getByRole('option', { name: 'Networking' })).toBeInTheDocument();
    });
    await userEvent.selectOptions(within(dialog).getByLabelText('Category'), 'c1');
    await userEvent.type(within(dialog).getByLabelText('Name'), 'Widget');
    await userEvent.type(within(dialog).getByLabelText('Unit price'), '12.50');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Create product' }));

    expect(await within(dialog).findByRole('alert')).toHaveTextContent('already exists');
    expect(onSaved).not.toHaveBeenCalled();
    const post = calls.find((c) => c.method === 'POST');
    expect(post?.headers['X-XSRF-TOKEN']).toBe('token-123');
    expect(post?.body).toMatchObject({ sku: 'ABC-1', unitPrice: 12.5, currency: 'USD', categoryId: 'c1' });
  });
});

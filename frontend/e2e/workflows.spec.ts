import { expect, test } from '@playwright/test';
import { login, stockOf, trackConsoleErrors, uniqueSku, users } from './support';

test.describe.configure({ mode: 'serial' });

test('manager runs the full product → stock → order → fulfilment workflow', async ({ page }) => {
  const assertNoConsoleErrors = trackConsoleErrors(page);
  const sku = uniqueSku('E2E');

  await test.step('login and dashboard', async () => {
    await login(page, users.manager());
    const summary = (await (await page.request.get('/api/v1/dashboard/summary')).json()) as { activeProducts: number };
    const card = page.getByRole('region', { name: 'Key metrics' });
    await expect(card.getByText('Active products')).toBeVisible();
    await expect(card.getByText(String(summary.activeProducts), { exact: true })).toBeVisible();
    await expect(page.getByText('Orders created · last 14 days')).toBeVisible();
  });

  let productId = '';
  await test.step('create product', async () => {
    await page.getByRole('link', { name: 'Products' }).click();
    await page.getByRole('button', { name: 'New product' }).click();
    const dialog = page.getByRole('dialog', { name: 'New product' });
    await dialog.getByLabel('SKU').fill(sku);
    await dialog.getByLabel('Category').selectOption({ label: 'Electronics' });
    await dialog.getByLabel('Name').fill('E2E Barcode Scanner');
    await dialog.getByLabel('Unit price').fill('25.50');
    await dialog.getByLabel('Reorder threshold').fill('2');
    await dialog.getByRole('button', { name: 'Create product' }).click();

    await expect(page.getByRole('heading', { name: 'E2E Barcode Scanner' })).toBeVisible();
    productId = page.url().split('/').pop() ?? '';
    const product = (await (await page.request.get(`/api/v1/products/${productId}`)).json()) as {
      sku: string;
      unitPrice: number;
    };
    expect(product.sku).toBe(sku);
    expect(product.unitPrice).toBe(25.5);
  });

  await test.step('receive stock through an inventory adjustment', async () => {
    await page.getByRole('button', { name: 'Adjust stock' }).click();
    const dialog = page.getByRole('dialog', { name: 'Adjust stock' });
    await dialog.getByLabel('Quantity').fill('8');
    await dialog.getByLabel('Note').fill('E2E receipt');
    await dialog.getByRole('button', { name: 'Save adjustment' }).click();
    await expect(dialog).toBeHidden();
    expect(await stockOf(page.request, productId)).toMatchObject({ onHand: 8, reserved: 0, available: 8 });
    await expect(page.getByRole('cell', { name: 'E2E receipt' })).toBeVisible();
  });

  let orderUrl = '';
  await test.step('create an order that reserves stock', async () => {
    await page.goto('/orders/new');
    await page.getByLabel('Customer name').fill('E2E Logistics');
    await page.getByLabel('Product 1').selectOption({ label: `${sku} — E2E Barcode Scanner (8 available)` });
    await page.getByLabel('Quantity').fill('3');
    await expect(page.getByText('$76.50')).toBeVisible();
    await page.getByRole('button', { name: 'Submit order' }).click();

    await expect(page.getByRole('heading', { name: /Order SO-\d+/ })).toBeVisible();
    await expect(page.getByText('Submitted', { exact: true }).first()).toBeVisible();
    orderUrl = page.url();
    expect(await stockOf(page.request, productId)).toMatchObject({ onHand: 8, reserved: 3, available: 5 });
  });

  await test.step('process and fulfil the order', async () => {
    await page.getByRole('button', { name: 'Start processing' }).click();
    await page.getByRole('alertdialog').getByRole('button', { name: 'Start processing' }).click();
    await expect(page.getByRole('button', { name: 'Fulfill' })).toBeVisible();

    await page.getByRole('button', { name: 'Fulfill' }).click();
    await page.getByRole('alertdialog').getByRole('button', { name: 'Fulfill order' }).click();
    await expect(page.getByText('Processing → Fulfilled')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Fulfill' })).toBeHidden();

    expect(await stockOf(page.request, productId)).toMatchObject({ onHand: 5, reserved: 0, available: 5 });
    const order = (await (await page.request.get(`/api/v1/orders/${orderUrl.split('/').pop() ?? ''}`)).json()) as {
      status: string;
    };
    expect(order.status).toBe('Fulfilled');
  });

  await test.step('inventory screen reflects the shipment', async () => {
    await page.goto(`/inventory?search=${sku}`);
    const row = page.getByRole('row', { name: new RegExp(sku) });
    await expect(row).toContainText('E2E Barcode Scanner');
    await expect(row.getByRole('cell').nth(2)).toHaveText('5');
  });

  assertNoConsoleErrors();
});

test('viewer can read but every write path is blocked in the UI and by the API', async ({ page }) => {
  const assertNoConsoleErrors = trackConsoleErrors(page);
  await login(page, users.viewer());

  await page.getByRole('link', { name: 'Products' }).click();
  await expect(page.getByRole('table')).toBeVisible();
  await expect(page.getByRole('button', { name: 'New product' })).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Users' })).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Activity' })).toHaveCount(0);

  await page.goto('/users');
  await expect(page.getByText("You don't have access to this page")).toBeVisible();

  // The UI hides controls, but the server is the authority: a crafted request is still rejected.
  const xsrf = (await page.context().cookies()).find((c) => c.name === 'XSRF-TOKEN')?.value ?? '';
  const response = await page.request.post('/api/v1/products', {
    headers: { 'X-XSRF-TOKEN': decodeURIComponent(xsrf) },
    data: {
      sku: 'VIEWER-1',
      name: 'x',
      description: null,
      categoryId: crypto.randomUUID(),
      unitPrice: 1,
      currency: 'USD',
      reorderThreshold: 1,
    },
  });
  expect(response.status()).toBe(403);
  assertNoConsoleErrors();
});

test('logout ends the session', async ({ page }) => {
  await login(page, users.admin());
  await page.getByRole('button', { name: 'Account menu' }).click();
  await page.getByRole('menuitem', { name: 'Sign out' }).click();

  await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible();
  expect((await page.request.get('/api/v1/auth/me')).status()).toBe(401);
  await page.goto('/orders');
  await expect(page).toHaveURL(/\/login$/);
});

test('invalid credentials show a generic error', async ({ page }) => {
  await page.goto('/login');
  await page.getByLabel('Email').fill('nobody@example.com');
  await page.getByLabel('Password').fill('definitely-wrong');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('alert')).toContainText('Invalid email or password');
});

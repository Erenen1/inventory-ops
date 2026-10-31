import { resolve } from 'node:path';
import { expect, test, type Page } from '@playwright/test';
import { login, users } from './support';

// Captures the README screenshots from the running stack (seeded demo data). Run explicitly:
//   npx playwright test --project=screenshots
const out = (name: string) => resolve(import.meta.dirname, '../../docs/screenshots', name);

async function selectBySku(page: Page, label: string, sku: string) {
  const select = page.getByLabel(label);
  const value = await select.locator('option', { hasText: sku }).getAttribute('value');
  await select.selectOption(value ?? '');
}

async function settle(page: Page) {
  await page.waitForLoadState('networkidle');
  await expect(page.locator('[aria-busy="true"]')).toHaveCount(0);
}

test('desktop screenshots', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 960 });
  await login(page, users.admin());

  // A tall viewport instead of fullPage keeps the fixed sidebar rendered at full height.
  await page.setViewportSize({ width: 1440, height: 1520 });
  await settle(page);
  await page.screenshot({ path: out('dashboard.png') });
  await page.setViewportSize({ width: 1440, height: 960 });

  await page.goto('/products');
  await settle(page);
  await page.screenshot({ path: out('products.png') });

  await page.goto('/inventory');
  await settle(page);
  await page.screenshot({ path: out('inventory.png') });

  await page.goto('/orders');
  await settle(page);
  await page.screenshot({ path: out('orders.png') });

  await page.getByRole('row').filter({ hasText: 'Processing' }).first().getByRole('link').click();
  await expect(page.getByRole('heading', { name: /Order SO-/ })).toBeVisible();
  await settle(page);
  await page.screenshot({ path: out('order-details.png') });

  await page.goto('/orders/new');
  await settle(page);
  await page.getByLabel('Customer name').fill('Northwind Traders');
  await page.getByLabel('Notes').fill('Deliver to loading dock B');
  await selectBySku(page, 'Product 1', 'ELC-MON-27Q');
  await page.getByLabel('Quantity').fill('4');
  await page.getByRole('button', { name: 'Add line' }).click();
  await selectBySku(page, 'Product 2', 'ELC-USB-C-HUB');
  await page.getByLabel('Quantity').nth(1).fill('6');
  await page.screenshot({ path: out('create-order.png') });

  await page.goto('/activity');
  await settle(page);
  await page.screenshot({ path: out('activity.png') });
});

test('mobile screenshot', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await login(page, users.admin());
  await settle(page);
  await page.screenshot({ path: out('mobile-dashboard.png'), fullPage: true });
});

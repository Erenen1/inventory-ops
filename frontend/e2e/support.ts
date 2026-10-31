import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { expect, type APIRequestContext, type Page } from '@playwright/test';

/** Credentials come from the git-ignored .env created by scripts/bootstrap (or from E2E_* variables). */
function loadEnv(): Record<string, string> {
  try {
    const text = readFileSync(resolve(import.meta.dirname, '../../.env'), 'utf8');
    return Object.fromEntries(
      text
        .split(/\r?\n/)
        .filter((line) => line.includes('=') && !line.trimStart().startsWith('#'))
        .map((line) => [line.slice(0, line.indexOf('=')).trim(), line.slice(line.indexOf('=') + 1).trim()]),
    );
  } catch {
    return {};
  }
}

const env = { ...loadEnv(), ...process.env } as Record<string, string | undefined>;

function credential(emailKey: string, passwordKey: string) {
  const email = env[emailKey];
  const password = env[passwordKey];
  if (!email || !password) throw new Error(`Set ${emailKey}/${passwordKey} (run scripts/bootstrap first).`);
  return { email, password };
}

export const users = {
  admin: () => credential('ADMIN_EMAIL', 'ADMIN_PASSWORD'),
  manager: () => credential('DEMO_MANAGER_EMAIL', 'DEMO_MANAGER_PASSWORD'),
  viewer: () => credential('DEMO_VIEWER_EMAIL', 'DEMO_VIEWER_PASSWORD'),
};

export async function login(page: Page, who: { email: string; password: string }) {
  await page.goto('/login');
  await page.getByLabel('Email').fill(who.email);
  await page.getByLabel('Password').fill(who.password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('heading', { name: 'Overview' })).toBeVisible();
}

/** Fails the test on uncaught page errors or console errors (e.g. React warnings, failed scripts). */
export function trackConsoleErrors(page: Page): () => void {
  const errors: string[] = [];
  page.on('pageerror', (error) => errors.push(error.message));
  page.on('console', (message) => {
    // 4xx responses the UI handles on purpose (e.g. 401 probe on /auth/me) are logged by the browser.
    if (message.type() === 'error' && !message.text().includes('Failed to load resource')) errors.push(message.text());
  });
  return () => {
    expect(errors, 'browser console errors').toEqual([]);
  };
}

export interface StockSnapshot {
  onHand: number;
  reserved: number;
  available: number;
}

/** Reads authoritative state through the API using the page's session cookie. */
export async function stockOf(request: APIRequestContext, productId: string): Promise<StockSnapshot> {
  const response = await request.get(`/api/v1/inventory/${productId}`);
  expect(response.status()).toBe(200);
  return (await response.json()) as StockSnapshot;
}

export const uniqueSku = (prefix: string) => `${prefix}-${Date.now().toString(36).toUpperCase()}`;

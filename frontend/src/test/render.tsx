import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render } from '@testing-library/react';
import type { ReactNode } from 'react';
import { MemoryRouter } from 'react-router';
import { AuthContext } from '@/features/auth/session';
import type { CurrentUser, Role } from '@/types/api';

export function userWithRole(role: Role): CurrentUser {
  const isAdmin = role === 'Admin';
  const canOperate = isAdmin || role === 'Manager';
  return {
    id: `${role}-id`,
    email: `${role.toLowerCase()}@example.com`,
    displayName: `${role} User`,
    role,
    permissions: { canOperate, canViewActivity: canOperate, canManageUsers: isAdmin },
  };
}

/** Renders with a fresh query client, router and a signed-in user (no network auth probe). */
export function renderWithProviders(ui: ReactNode, { role, route = '/' }: { role?: Role; route?: string } = {}) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[route]}>
        <AuthContext value={{ user: userWithRole(role ?? 'Admin'), isLoading: false }}>{ui}</AuthContext>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

/** Minimal fetch stub: maps "METHOD /path" (path without query) to a JSON body and status. */
export function stubFetch(
  routes: Record<string, { status?: number; body?: unknown } | (() => { status?: number; body?: unknown })>,
) {
  const calls: { method: string; url: string; headers: Record<string, string>; body?: unknown }[] = [];
  const fetchMock = vi.fn((input: string, init?: RequestInit) => {
    const url = input;
    const method = init?.method ?? 'GET';
    calls.push({
      method,
      url,
      headers: (init?.headers ?? {}) as Record<string, string>,
      body: typeof init?.body === 'string' ? JSON.parse(init.body) : undefined,
    });
    const entry = routes[`${method} ${url.split('?')[0] ?? ''}`];
    const resolved = typeof entry === 'function' ? entry() : entry;
    const status = resolved?.status ?? (resolved ? 200 : 404);
    const body = resolved?.body ?? (resolved ? undefined : { title: 'Not found', status: 404 });
    return Promise.resolve(
      new Response(status === 204 ? null : JSON.stringify(body), {
        status,
        headers: { 'content-type': 'application/problem+json' },
      }),
    );
  });
  vi.stubGlobal('fetch', fetchMock);
  return calls;
}

import { screen } from '@testing-library/react';
import { Route, Routes } from 'react-router';
import { AppShell } from '@/app/AppShell';
import { RequirePermission } from '@/app/route-elements';
import { ApiError, apiRequest, setUnauthorizedHandler } from '@/lib/api';
import { renderWithProviders, stubFetch } from '@/test/render';

afterEach(() => {
  vi.unstubAllGlobals();
  setUnauthorizedHandler(undefined);
});

describe('navigation reflects permissions', () => {
  it.each([
    ['Viewer', false, false],
    ['Manager', true, false],
    ['Admin', true, true],
  ] as const)('%s sees activity=%s users=%s', (role, activity, users) => {
    renderWithProviders(
      <Routes>
        <Route element={<AppShell />}>
          <Route index element={<p>home</p>} />
        </Route>
      </Routes>,
      { role },
    );
    const nav = screen.getAllByRole('navigation', { name: 'Main' })[0];
    expect(nav).toHaveTextContent('Products');
    expect(nav?.textContent.includes('Activity')).toBe(activity);
    expect(nav?.textContent.includes('Users')).toBe(users);
  });

  it('blocks direct navigation to pages the role cannot use', () => {
    renderWithProviders(
      <RequirePermission allow={(u) => u.permissions.canManageUsers}>
        <p>secret</p>
      </RequirePermission>,
      { role: 'Manager' },
    );
    expect(screen.queryByText('secret')).not.toBeInTheDocument();
    expect(screen.getByText("You don't have access to this page")).toBeInTheDocument();
  });
});

describe('api client', () => {
  it('turns problem details into ApiError and notifies on expired sessions', async () => {
    stubFetch({
      'GET /api/v1/orders': { status: 401, body: { title: 'Authentication required', code: 'unauthenticated' } },
    });
    const onUnauthorized = vi.fn();
    setUnauthorizedHandler(onUnauthorized);

    const error = await apiRequest('/orders').catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).code).toBe('unauthenticated');
    expect(onUnauthorized).toHaveBeenCalledOnce();
  });

  it('fetches a CSRF token before the first unsafe request', async () => {
    document.cookie = 'XSRF-TOKEN=; expires=Thu, 01 Jan 1970 00:00:00 GMT';
    const calls = stubFetch({
      'GET /api/v1/auth/csrf': () => {
        document.cookie = 'XSRF-TOKEN=fresh%2Btoken';
        return { status: 204 };
      },
      'POST /api/v1/categories': { status: 201, body: { id: 'c1' } },
    });

    await apiRequest('/categories', { method: 'POST', body: { name: 'New' } });

    expect(calls.map((c) => `${c.method} ${c.url}`)).toEqual(['GET /api/v1/auth/csrf', 'POST /api/v1/categories']);
    expect(calls[1]?.headers['X-XSRF-TOKEN']).toBe('fresh+token');
  });
});

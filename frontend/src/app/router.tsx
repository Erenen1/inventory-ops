import { lazy } from 'react';
import { createBrowserRouter } from 'react-router';
import { AppShell } from '@/app/AppShell';
import { NotFound, RequirePermission, Root } from '@/app/route-elements';
import { RequireAuth } from '@/features/auth/AuthProvider';
import { LoginPage } from '@/features/auth/LoginPage';

// Screens are code-split per route; the shell and login load eagerly.
const ActivityPage = lazy(() => import('@/features/activity/ActivityPage').then((m) => ({ default: m.ActivityPage })));
const DashboardPage = lazy(() =>
  import('@/features/dashboard/DashboardPage').then((m) => ({ default: m.DashboardPage })),
);
const InventoryPage = lazy(() =>
  import('@/features/inventory/InventoryPage').then((m) => ({ default: m.InventoryPage })),
);
const CreateOrderPage = lazy(() =>
  import('@/features/orders/CreateOrderPage').then((m) => ({ default: m.CreateOrderPage })),
);
const OrderDetailPage = lazy(() =>
  import('@/features/orders/OrderDetailPage').then((m) => ({ default: m.OrderDetailPage })),
);
const OrdersPage = lazy(() => import('@/features/orders/OrdersPage').then((m) => ({ default: m.OrdersPage })));
const ProductDetailPage = lazy(() =>
  import('@/features/products/ProductDetailPage').then((m) => ({ default: m.ProductDetailPage })),
);
const ProductsPage = lazy(() => import('@/features/products/ProductsPage').then((m) => ({ default: m.ProductsPage })));
const UsersPage = lazy(() => import('@/features/users/UsersPage').then((m) => ({ default: m.UsersPage })));

export const router = createBrowserRouter([
  {
    element: <Root />,
    children: [
      { path: '/login', element: <LoginPage /> },
      {
        element: (
          <RequireAuth>
            <AppShell />
          </RequireAuth>
        ),
        children: [
          { index: true, element: <DashboardPage /> },
          { path: 'products', element: <ProductsPage /> },
          { path: 'products/:id', element: <ProductDetailPage /> },
          { path: 'inventory', element: <InventoryPage /> },
          { path: 'orders', element: <OrdersPage /> },
          {
            path: 'orders/new',
            element: (
              <RequirePermission allow={(u) => u.permissions.canOperate}>
                <CreateOrderPage />
              </RequirePermission>
            ),
          },
          { path: 'orders/:id', element: <OrderDetailPage /> },
          {
            path: 'activity',
            element: (
              <RequirePermission allow={(u) => u.permissions.canViewActivity}>
                <ActivityPage />
              </RequirePermission>
            ),
          },
          {
            path: 'users',
            element: (
              <RequirePermission allow={(u) => u.permissions.canManageUsers}>
                <UsersPage />
              </RequirePermission>
            ),
          },
          { path: '*', element: <NotFound /> },
        ],
      },
    ],
  },
]);

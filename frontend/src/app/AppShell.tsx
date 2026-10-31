import * as DialogPrimitive from '@radix-ui/react-dialog';
import {
  Activity,
  Boxes,
  ChevronsUpDown,
  LayoutDashboard,
  LogOut,
  Menu,
  Package,
  ShoppingCart,
  Users,
  Warehouse,
  X,
} from 'lucide-react';
import { Suspense, useState, type ReactNode } from 'react';
import { NavLink, Outlet } from 'react-router';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/overlays';
import { useCurrentUser, useLogout } from '@/features/auth/session';
import { cn } from '@/lib/utils';
import type { CurrentUser } from '@/types/api';

interface NavItem {
  to: string;
  label: string;
  icon: ReactNode;
  visible?: (user: CurrentUser) => boolean;
}

const navItems: NavItem[] = [
  { to: '/', label: 'Overview', icon: <LayoutDashboard /> },
  { to: '/products', label: 'Products', icon: <Package /> },
  { to: '/inventory', label: 'Inventory', icon: <Warehouse /> },
  { to: '/orders', label: 'Orders', icon: <ShoppingCart /> },
  { to: '/activity', label: 'Activity', icon: <Activity />, visible: (u) => u.permissions.canViewActivity },
  { to: '/users', label: 'Users', icon: <Users />, visible: (u) => u.permissions.canManageUsers },
];

function Brand() {
  return (
    <div className="flex h-14 items-center gap-2.5 px-4">
      <span className="rounded-md bg-brand-700 p-1.5 text-white">
        <Boxes className="size-4" aria-hidden />
      </span>
      <span className="font-semibold tracking-tight">InventoryOps</span>
    </div>
  );
}

function Navigation({ user, onNavigate }: { user: CurrentUser; onNavigate?: () => void }) {
  return (
    <nav aria-label="Main" className="flex-1 space-y-0.5 px-3 py-2">
      {navItems
        .filter((item) => item.visible?.(user) ?? true)
        .map((item) => (
          <NavLink
            key={item.to}
            to={item.to}
            end={item.to === '/'}
            onClick={onNavigate}
            className={({ isActive }) =>
              cn(
                'flex items-center gap-2.5 rounded-md px-2.5 py-2 text-sm font-medium transition-colors [&_svg]:size-4',
                isActive ? 'bg-brand-50 text-brand-800' : 'text-zinc-600 hover:bg-zinc-100 hover:text-zinc-900',
              )
            }
          >
            {item.icon}
            {item.label}
          </NavLink>
        ))}
    </nav>
  );
}

function UserMenu({ user }: { user: CurrentUser }) {
  const logout = useLogout();
  const initials = user.displayName
    .split(' ')
    .map((part) => part[0])
    .join('')
    .slice(0, 2)
    .toUpperCase();
  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        className="flex w-full items-center gap-2.5 rounded-md p-2 text-left hover:bg-zinc-100"
        aria-label="Account menu"
      >
        <span className="flex size-8 shrink-0 items-center justify-center rounded-full bg-zinc-200 text-xs font-semibold text-zinc-700">
          {initials}
        </span>
        <span className="min-w-0 flex-1">
          <span className="block truncate text-sm font-medium text-zinc-900">{user.displayName}</span>
          <span className="block truncate text-xs text-zinc-500">{user.role}</span>
        </span>
        <ChevronsUpDown className="size-4 text-zinc-400" aria-hidden />
      </DropdownMenuTrigger>
      <DropdownMenuContent align="start" side="top" className="w-56">
        <div className="px-2 py-1.5 text-xs text-zinc-500">{user.email}</div>
        <DropdownMenuSeparator />
        <DropdownMenuItem
          onSelect={() => {
            logout.mutate();
          }}
        >
          <LogOut /> Sign out
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}

export function AppShell() {
  const user = useCurrentUser();
  const [mobileOpen, setMobileOpen] = useState(false);
  return (
    <div className="min-h-dvh lg:pl-60">
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:absolute focus:top-2 focus:left-2 focus:z-50 focus:rounded focus:bg-white focus:px-3 focus:py-2"
      >
        Skip to content
      </a>

      <aside className="fixed inset-y-0 left-0 hidden w-60 flex-col border-r border-zinc-200 bg-white lg:flex">
        <Brand />
        <Navigation user={user} />
        <div className="border-t border-zinc-100 p-2">
          <UserMenu user={user} />
        </div>
      </aside>

      <header className="sticky top-0 z-30 flex h-14 items-center justify-between border-b border-zinc-200 bg-white/95 px-4 backdrop-blur lg:hidden">
        <div className="flex items-center gap-2">
          <span className="rounded-md bg-brand-700 p-1.5 text-white">
            <Boxes className="size-4" aria-hidden />
          </span>
          <span className="font-semibold tracking-tight">InventoryOps</span>
        </div>
        <DialogPrimitive.Root open={mobileOpen} onOpenChange={setMobileOpen}>
          <DialogPrimitive.Trigger
            className="rounded-md p-2 text-zinc-700 hover:bg-zinc-100"
            aria-label="Open navigation"
          >
            <Menu className="size-5" />
          </DialogPrimitive.Trigger>
          <DialogPrimitive.Portal>
            <DialogPrimitive.Overlay className="fixed inset-0 z-40 bg-zinc-950/40" />
            <DialogPrimitive.Content className="fixed inset-y-0 left-0 z-50 flex w-72 flex-col bg-white shadow-xl focus:outline-none">
              <DialogPrimitive.Title className="sr-only">Navigation</DialogPrimitive.Title>
              <DialogPrimitive.Description className="sr-only">Main navigation</DialogPrimitive.Description>
              <div className="flex items-center justify-between pr-2">
                <Brand />
                <DialogPrimitive.Close
                  className="rounded-md p-2 text-zinc-600 hover:bg-zinc-100"
                  aria-label="Close navigation"
                >
                  <X className="size-5" />
                </DialogPrimitive.Close>
              </div>
              <Navigation
                user={user}
                onNavigate={() => {
                  setMobileOpen(false);
                }}
              />
              <div className="border-t border-zinc-100 p-2">
                <UserMenu user={user} />
              </div>
            </DialogPrimitive.Content>
          </DialogPrimitive.Portal>
        </DialogPrimitive.Root>
      </header>

      <main id="main" className="mx-auto max-w-7xl px-4 py-6 sm:px-6 lg:px-8 lg:py-8">
        <Suspense
          fallback={
            <div role="status" className="py-24 text-center text-sm text-zinc-500">
              Loading…
            </div>
          }
        >
          <Outlet />
        </Suspense>
      </main>
    </div>
  );
}

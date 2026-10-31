import { FileQuestion, ShieldAlert } from 'lucide-react';
import type { ReactNode } from 'react';
import { Link, Outlet } from 'react-router';
import { EmptyState } from '@/components/shared/page';
import { Card } from '@/components/ui/surface';
import { AuthProvider } from '@/features/auth/AuthProvider';
import { useCurrentUser } from '@/features/auth/session';
import type { CurrentUser } from '@/types/api';

/**
 * Hides screens the user's role cannot use. This is a usability measure only: every API endpoint
 * enforces the same rules server-side.
 */
export function RequirePermission({ allow, children }: { allow: (user: CurrentUser) => boolean; children: ReactNode }) {
  const user = useCurrentUser();
  if (!allow(user)) {
    return (
      <Card>
        <EmptyState
          icon={<ShieldAlert />}
          title="You don't have access to this page"
          description="Ask an administrator if you need this permission."
          action={
            <Link to="/" className="text-sm text-brand-700 hover:underline">
              Back to overview
            </Link>
          }
        />
      </Card>
    );
  }
  return children;
}

export function NotFound() {
  return (
    <Card>
      <EmptyState
        icon={<FileQuestion />}
        title="Page not found"
        action={
          <Link to="/" className="text-sm text-brand-700 hover:underline">
            Back to overview
          </Link>
        }
      />
    </Card>
  );
}

export function Root() {
  return (
    <AuthProvider>
      <Outlet />
    </AuthProvider>
  );
}

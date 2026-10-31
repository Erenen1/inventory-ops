import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, type ReactNode } from 'react';
import { Navigate, useLocation, useNavigate } from 'react-router';
import { AuthContext, fetchMe, meQueryKey, useAuth } from '@/features/auth/session';
import { setUnauthorizedHandler } from '@/lib/api';

export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const location = useLocation();
  const { data, isLoading } = useQuery({ queryKey: meQueryKey, queryFn: fetchMe, staleTime: 5 * 60_000, retry: false });

  // Any 401 from a data request means the session expired or was revoked (e.g. role change).
  useEffect(() => {
    setUnauthorizedHandler(() => {
      queryClient.setQueryData(meQueryKey, null);
      queryClient.removeQueries({ predicate: (q) => q.queryKey[0] !== 'auth' });
      void navigate('/login', { replace: true, state: { from: location.pathname, expired: true } });
    });
    return () => {
      setUnauthorizedHandler(undefined);
    };
  }, [queryClient, navigate, location.pathname]);

  return <AuthContext value={{ user: data ?? null, isLoading }}>{children}</AuthContext>;
}

export function RequireAuth({ children }: { children: ReactNode }) {
  const { user, isLoading } = useAuth();
  const location = useLocation();
  if (isLoading) {
    return (
      <div className="flex min-h-dvh items-center justify-center text-sm text-zinc-500" role="status">
        Loading your workspace…
      </div>
    );
  }
  if (!user) return <Navigate to="/login" replace state={{ from: location.pathname }} />;
  return children;
}

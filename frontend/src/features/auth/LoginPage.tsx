import { zodResolver } from '@hookform/resolvers/zod';
import { AlertCircle, Boxes } from 'lucide-react';
import { useForm } from 'react-hook-form';
import { Navigate, useLocation, useNavigate } from 'react-router';
import { z } from 'zod';
import { FormField } from '@/components/shared/page';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/form-controls';
import { useAuth, useLogin } from '@/features/auth/session';
import { ApiError, errorMessage } from '@/lib/api';

const loginSchema = z.object({
  email: z.email('Enter a valid email address.'),
  password: z.string().min(1, 'Enter your password.'),
});
type LoginValues = z.infer<typeof loginSchema>;

interface LocationState {
  from?: string;
  expired?: boolean;
}

export function LoginPage() {
  const { user } = useAuth();
  const login = useLogin();
  const navigate = useNavigate();
  const state = (useLocation().state ?? {}) as LocationState;
  const form = useForm<LoginValues>({ resolver: zodResolver(loginSchema), defaultValues: { email: '', password: '' } });

  if (user) return <Navigate to={state.from ?? '/'} replace />;

  const onSubmit = form.handleSubmit((values) => {
    login.mutate(values, { onSuccess: () => void navigate(state.from ?? '/', { replace: true }) });
  });

  const failure = login.error
    ? login.error instanceof ApiError && login.error.status === 401
      ? 'Invalid email or password, or the account is temporarily locked.'
      : errorMessage(login.error)
    : state.expired
      ? 'Your session has expired. Please sign in again.'
      : null;

  const { errors } = form.formState;
  return (
    <main className="flex min-h-dvh items-center justify-center px-4 py-12">
      <div className="w-full max-w-sm">
        <div className="mb-8 flex items-center justify-center gap-2.5">
          <span className="rounded-lg bg-brand-700 p-2 text-white">
            <Boxes className="size-5" aria-hidden />
          </span>
          <span className="text-lg font-semibold tracking-tight">InventoryOps</span>
        </div>
        <div className="rounded-xl border border-zinc-200 bg-white p-6 shadow-sm">
          <h1 className="text-base font-semibold">Sign in</h1>
          <p className="mt-1 text-sm text-zinc-500">Use the account provided by your administrator.</p>

          {failure && (
            <div
              role="alert"
              className="mt-4 flex gap-2 rounded-md border border-red-200 bg-red-50 px-3 py-2.5 text-sm text-red-700"
            >
              <AlertCircle className="mt-0.5 size-4 shrink-0" aria-hidden />
              {failure}
            </div>
          )}

          <form className="mt-5 space-y-4" onSubmit={onSubmit} noValidate>
            <FormField id="email" label="Email" error={errors.email?.message}>
              <Input
                id="email"
                type="email"
                autoComplete="username"
                autoFocus
                aria-invalid={!!errors.email}
                aria-describedby={errors.email ? 'email-error' : undefined}
                {...form.register('email')}
              />
            </FormField>
            <FormField id="password" label="Password" error={errors.password?.message}>
              <Input
                id="password"
                type="password"
                autoComplete="current-password"
                aria-invalid={!!errors.password}
                aria-describedby={errors.password ? 'password-error' : undefined}
                {...form.register('password')}
              />
            </FormField>
            <Button type="submit" className="w-full" loading={login.isPending}>
              Sign in
            </Button>
          </form>
        </div>
        <p className="mt-6 text-center text-xs text-zinc-500">Inventory &amp; order management</p>
      </div>
    </main>
  );
}

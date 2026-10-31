import { zodResolver } from '@hookform/resolvers/zod';
import { UserPlus } from 'lucide-react';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { toast } from 'sonner';
import { z } from 'zod';
import { EmptyState, ErrorState, FormField, LoadingRows, PageHeader, Pagination } from '@/components/shared/page';
import { Button } from '@/components/ui/button';
import { Input, Select } from '@/components/ui/form-controls';
import { Dialog, DialogClose, DialogContent, DialogFooter, DialogTrigger } from '@/components/ui/overlays';
import { Badge, Card, Table, Td, Th } from '@/components/ui/surface';
import { useCurrentUser } from '@/features/auth/session';
import { useChangeRole, useCreateUser, useSetUserActive, useUsers } from '@/features/users/api';
import { errorMessage } from '@/lib/api';
import { applyServerErrors } from '@/lib/forms';
import { formatDate } from '@/lib/format';
import type { Role } from '@/types/api';

const roles: Role[] = ['Admin', 'Manager', 'Viewer'];

const userSchema = z.object({
  email: z.email('Enter a valid email address.'),
  displayName: z.string().trim().min(1, 'Name is required.').max(100),
  role: z.enum(['Admin', 'Manager', 'Viewer']),
  password: z
    .string()
    .min(12, 'Use at least 12 characters.')
    .max(128)
    .regex(/[a-z]/, 'Include a lowercase letter.')
    .regex(/[A-Z]/, 'Include an uppercase letter.')
    .regex(/\d/, 'Include a digit.'),
});
type UserValues = z.infer<typeof userSchema>;

export function UsersPage() {
  const me = useCurrentUser();
  const [page, setPage] = useState(1);
  const [createOpen, setCreateOpen] = useState(false);
  const { data, isLoading, error, refetch, isFetching } = useUsers(page);
  const changeRole = useChangeRole();
  const setActive = useSetUserActive();

  const onError = (e: unknown) => toast.error(errorMessage(e));

  return (
    <>
      <PageHeader
        title="Users"
        description="Accounts and roles. Role and status changes take effect within a minute for active sessions."
        actions={
          <Dialog open={createOpen} onOpenChange={setCreateOpen}>
            <DialogTrigger asChild>
              <Button>
                <UserPlus /> New user
              </Button>
            </DialogTrigger>
            {createOpen && (
              <CreateUserDialog
                onDone={() => {
                  setCreateOpen(false);
                }}
              />
            )}
          </Dialog>
        }
      />
      <Card>
        {error ? (
          <ErrorState error={error} onRetry={() => void refetch()} />
        ) : (
          <>
            <Table>
              <thead>
                <tr>
                  <Th>User</Th>
                  <Th>Role</Th>
                  <Th>Status</Th>
                  <Th className="hidden md:table-cell">Created</Th>
                  <Th className="text-right">
                    <span className="sr-only">Actions</span>
                  </Th>
                </tr>
              </thead>
              <tbody>
                {isLoading ? (
                  <LoadingRows columns={5} rows={3} />
                ) : (
                  data?.items.map((u) => {
                    const isSelf = u.id === me.id;
                    return (
                      <tr key={u.id}>
                        <Td>
                          <span className="block font-medium text-zinc-900">
                            {u.displayName} {isSelf && <span className="text-xs font-normal text-zinc-500">(you)</span>}
                          </span>
                          <span className="text-xs text-zinc-500">{u.email}</span>
                        </Td>
                        <Td>
                          <Select
                            aria-label={`Role for ${u.email}`}
                            className="h-8 w-32"
                            value={u.role}
                            disabled={isSelf || changeRole.isPending}
                            onChange={(e) => {
                              changeRole.mutate(
                                { id: u.id, role: e.target.value as Role },
                                { onSuccess: () => toast.success('Role updated'), onError },
                              );
                            }}
                          >
                            {roles.map((r) => (
                              <option key={r}>{r}</option>
                            ))}
                          </Select>
                        </Td>
                        <Td>{u.isActive ? <Badge tone="success">Active</Badge> : <Badge>Deactivated</Badge>}</Td>
                        <Td className="hidden text-xs text-zinc-500 md:table-cell">{formatDate(u.createdAt)}</Td>
                        <Td className="text-right">
                          {!isSelf && (
                            <Button
                              variant="secondary"
                              size="sm"
                              loading={setActive.isPending && setActive.variables.id === u.id}
                              onClick={() => {
                                setActive.mutate(
                                  { id: u.id, isActive: !u.isActive },
                                  {
                                    onSuccess: () =>
                                      toast.success(u.isActive ? 'User deactivated' : 'User reactivated'),
                                    onError,
                                  },
                                );
                              }}
                            >
                              {u.isActive ? 'Deactivate' : 'Reactivate'}
                            </Button>
                          )}
                        </Td>
                      </tr>
                    );
                  })
                )}
              </tbody>
            </Table>
            {data?.items.length === 0 && <EmptyState title="No users" />}
            {data && data.totalCount > data.pageSize && (
              <Pagination
                page={page}
                pageSize={data.pageSize}
                totalCount={data.totalCount}
                onPageChange={setPage}
                isFetching={isFetching}
              />
            )}
          </>
        )}
      </Card>
    </>
  );
}

function CreateUserDialog({ onDone }: { onDone: () => void }) {
  const create = useCreateUser();
  const [formError, setFormError] = useState<string | null>(null);
  const form = useForm<UserValues>({
    resolver: zodResolver(userSchema),
    defaultValues: { email: '', displayName: '', role: 'Viewer', password: '' },
  });
  const { errors } = form.formState;

  const onSubmit = form.handleSubmit(async (values) => {
    setFormError(null);
    try {
      await create.mutateAsync(values);
      toast.success(`User ${values.email} created`);
      onDone();
    } catch (error) {
      setFormError(applyServerErrors(error, form.setError, ['email', 'displayName', 'role', 'password']));
    }
  });

  return (
    <DialogContent title="New user" description="Share the initial password through a secure channel.">
      <form onSubmit={onSubmit} noValidate className="space-y-4">
        {formError && (
          <p role="alert" className="rounded-md border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
            {formError}
          </p>
        )}
        <FormField id="user-email" label="Email" error={errors.email?.message}>
          <Input
            id="user-email"
            type="email"
            autoComplete="off"
            aria-invalid={!!errors.email}
            {...form.register('email')}
          />
        </FormField>
        <div className="grid gap-4 sm:grid-cols-2">
          <FormField id="user-name" label="Display name" error={errors.displayName?.message}>
            <Input id="user-name" aria-invalid={!!errors.displayName} {...form.register('displayName')} />
          </FormField>
          <FormField id="user-role" label="Role" error={errors.role?.message}>
            <Select id="user-role" {...form.register('role')}>
              {roles.map((r) => (
                <option key={r}>{r}</option>
              ))}
            </Select>
          </FormField>
        </div>
        <FormField
          id="user-password"
          label="Initial password"
          error={errors.password?.message}
          hint="At least 12 characters with upper- and lowercase letters and a digit."
        >
          <Input
            id="user-password"
            type="password"
            autoComplete="new-password"
            aria-invalid={!!errors.password}
            {...form.register('password')}
          />
        </FormField>
        <DialogFooter>
          <DialogClose asChild>
            <Button type="button" variant="secondary">
              Cancel
            </Button>
          </DialogClose>
          <Button type="submit" loading={form.formState.isSubmitting}>
            Create user
          </Button>
        </DialogFooter>
      </form>
    </DialogContent>
  );
}

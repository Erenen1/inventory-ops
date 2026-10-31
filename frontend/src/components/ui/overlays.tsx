import * as AlertDialogPrimitive from '@radix-ui/react-alert-dialog';
import * as DialogPrimitive from '@radix-ui/react-dialog';
import * as DropdownPrimitive from '@radix-ui/react-dropdown-menu';
import { X } from 'lucide-react';
import type { ComponentProps, ReactNode } from 'react';
import { buttonVariants } from '@/components/ui/button';
import { cn } from '@/lib/utils';

const overlay = 'fixed inset-0 z-40 bg-zinc-950/40';
const panel =
  'fixed top-1/2 left-1/2 z-50 w-[calc(100%-2rem)] max-w-lg -translate-x-1/2 -translate-y-1/2 rounded-lg border border-zinc-200 bg-white shadow-xl focus:outline-none';

export const Dialog = DialogPrimitive.Root;
export const DialogTrigger = DialogPrimitive.Trigger;
export const DialogClose = DialogPrimitive.Close;

interface DialogContentProps extends ComponentProps<typeof DialogPrimitive.Content> {
  title: string;
  description?: ReactNode;
}

/** Accessible modal: focus is trapped, Escape closes, the title labels the dialog. */
export function DialogContent({ title, description, className, children, ...props }: DialogContentProps) {
  return (
    <DialogPrimitive.Portal>
      <DialogPrimitive.Overlay className={overlay} />
      <DialogPrimitive.Content className={cn(panel, 'max-h-[calc(100dvh-2rem)] overflow-y-auto', className)} {...props}>
        <div className="flex items-start justify-between gap-4 border-b border-zinc-100 px-5 py-4">
          <div>
            <DialogPrimitive.Title className="text-base font-semibold text-zinc-900">{title}</DialogPrimitive.Title>
            {description ? (
              <DialogPrimitive.Description className="mt-1 text-sm text-zinc-500">
                {description}
              </DialogPrimitive.Description>
            ) : (
              <DialogPrimitive.Description className="sr-only">{title}</DialogPrimitive.Description>
            )}
          </div>
          <DialogPrimitive.Close
            className="rounded-md p-1 text-zinc-500 hover:bg-zinc-100 hover:text-zinc-800"
            aria-label="Close"
          >
            <X className="size-4" />
          </DialogPrimitive.Close>
        </div>
        <div className="px-5 py-4">{children}</div>
      </DialogPrimitive.Content>
    </DialogPrimitive.Portal>
  );
}

export function DialogFooter({ children }: { children: ReactNode }) {
  return (
    <div className="-mx-5 mt-5 -mb-4 flex justify-end gap-2 border-t border-zinc-100 bg-zinc-50/60 px-5 py-3">
      {children}
    </div>
  );
}

interface ConfirmDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  description: ReactNode;
  confirmLabel: string;
  tone?: 'primary' | 'danger';
  loading?: boolean;
  onConfirm: () => void;
  children?: ReactNode;
}

/** Confirmation for consequential actions (cancel order, deactivate product, fulfill). */
export function ConfirmDialog({
  open,
  onOpenChange,
  title,
  description,
  confirmLabel,
  tone = 'primary',
  loading,
  onConfirm,
  children,
}: ConfirmDialogProps) {
  return (
    <AlertDialogPrimitive.Root open={open} onOpenChange={onOpenChange}>
      <AlertDialogPrimitive.Portal>
        <AlertDialogPrimitive.Overlay className={overlay} />
        <AlertDialogPrimitive.Content className={cn(panel, 'max-w-md p-5')}>
          <AlertDialogPrimitive.Title className="text-base font-semibold text-zinc-900">
            {title}
          </AlertDialogPrimitive.Title>
          <AlertDialogPrimitive.Description className="mt-2 text-sm text-zinc-600">
            {description}
          </AlertDialogPrimitive.Description>
          {children}
          <div className="mt-5 flex justify-end gap-2">
            <AlertDialogPrimitive.Cancel className={buttonVariants({ variant: 'secondary' })} disabled={loading}>
              Keep as is
            </AlertDialogPrimitive.Cancel>
            <button
              type="button"
              className={buttonVariants({ variant: tone })}
              disabled={loading}
              aria-busy={loading}
              onClick={onConfirm}
            >
              {confirmLabel}
            </button>
          </div>
        </AlertDialogPrimitive.Content>
      </AlertDialogPrimitive.Portal>
    </AlertDialogPrimitive.Root>
  );
}

export const DropdownMenu = DropdownPrimitive.Root;
export const DropdownMenuTrigger = DropdownPrimitive.Trigger;

export function DropdownMenuContent({ className, ...props }: ComponentProps<typeof DropdownPrimitive.Content>) {
  return (
    <DropdownPrimitive.Portal>
      <DropdownPrimitive.Content
        sideOffset={6}
        className={cn('z-50 min-w-48 rounded-md border border-zinc-200 bg-white p-1 shadow-lg', className)}
        {...props}
      />
    </DropdownPrimitive.Portal>
  );
}

export function DropdownMenuItem({ className, ...props }: ComponentProps<typeof DropdownPrimitive.Item>) {
  return (
    <DropdownPrimitive.Item
      className={cn(
        'flex cursor-default items-center gap-2 rounded px-2 py-1.5 text-sm text-zinc-700 outline-none select-none data-[highlighted]:bg-zinc-100 data-[highlighted]:text-zinc-900 [&_svg]:size-4',
        className,
      )}
      {...props}
    />
  );
}

export const DropdownMenuLabel = DropdownPrimitive.Label;
export const DropdownMenuSeparator = (props: ComponentProps<typeof DropdownPrimitive.Separator>) => (
  <DropdownPrimitive.Separator className="my-1 h-px bg-zinc-100" {...props} />
);

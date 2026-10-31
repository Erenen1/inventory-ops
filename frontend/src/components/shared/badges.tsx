import { Badge } from '@/components/ui/surface';
import type { OrderStatus } from '@/types/api';

const orderTone = {
  Submitted: 'info',
  Processing: 'warning',
  Fulfilled: 'success',
  Cancelled: 'neutral',
} as const satisfies Record<OrderStatus, string>;

export function OrderStatusBadge({ status }: { status: OrderStatus }) {
  return <Badge tone={orderTone[status]}>{status}</Badge>;
}

export function StockBadge({ available, isLowStock }: { available: number; isLowStock: boolean }) {
  if (available === 0) return <Badge tone="danger">Out of stock</Badge>;
  if (isLowStock) return <Badge tone="warning">Low stock</Badge>;
  return <Badge tone="success">In stock</Badge>;
}

export function ActiveBadge({ isActive }: { isActive: boolean }) {
  return isActive ? <Badge tone="brand">Active</Badge> : <Badge>Inactive</Badge>;
}

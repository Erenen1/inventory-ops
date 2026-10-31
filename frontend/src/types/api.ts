// Typed contracts mirroring the backend DTOs (InventoryOps.Application). Enums are serialized as strings.

export type Role = 'Admin' | 'Manager' | 'Viewer';
export type OrderStatus = 'Submitted' | 'Processing' | 'Fulfilled' | 'Cancelled';
export type AdjustmentReason = 'Received' | 'CountCorrection' | 'Damaged' | 'Lost' | 'Returned' | 'Other';
export type ActivitySeverity = 'Info' | 'Warning';
export type SortDirection = 'Asc' | 'Desc';

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface CurrentUser {
  id: string;
  email: string;
  displayName: string;
  role: Role;
  permissions: { canOperate: boolean; canViewActivity: boolean; canManageUsers: boolean };
}

export interface Category {
  id: string;
  name: string;
  productCount: number;
}

export interface ProductListItem {
  id: string;
  sku: string;
  name: string;
  categoryId: string;
  categoryName: string;
  unitPrice: number;
  currency: string;
  isActive: boolean;
  onHand: number;
  reserved: number;
  available: number;
  updatedAt: string;
}

export interface ProductDetails {
  id: string;
  sku: string;
  name: string;
  description: string | null;
  categoryId: string;
  categoryName: string;
  unitPrice: number;
  currency: string;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface InventoryItem {
  productId: string;
  sku: string;
  productName: string;
  categoryName: string;
  isActive: boolean;
  onHand: number;
  reserved: number;
  available: number;
  reorderThreshold: number;
  isLowStock: boolean;
  updatedAt: string;
}

export interface StockMovement {
  id: number;
  type: 'Adjustment' | 'Reservation' | 'ReservationRelease' | 'Fulfillment';
  onHandChange: number;
  reservedChange: number;
  onHandAfter: number;
  reservedAfter: number;
  reason: AdjustmentReason | null;
  note: string | null;
  orderId: string | null;
  orderNumber: string | null;
  performedBy: string;
  occurredAt: string;
}

export interface OrderListItem {
  id: string;
  number: string;
  customerName: string;
  status: OrderStatus;
  lineCount: number;
  totalQuantity: number;
  totalAmount: number;
  currency: string;
  createdBy: string;
  createdAt: string;
  updatedAt: string;
}

export interface OrderLine {
  id: string;
  productId: string;
  sku: string;
  productName: string;
  unitPrice: number;
  quantity: number;
  lineTotal: number;
}

export interface OrderStatusChange {
  fromStatus: OrderStatus | null;
  toStatus: OrderStatus;
  changedBy: string;
  reason: string | null;
  changedAt: string;
}

export interface OrderDetails {
  id: string;
  number: string;
  customerName: string;
  notes: string | null;
  status: OrderStatus;
  totalAmount: number;
  currency: string;
  createdBy: string;
  createdAt: string;
  updatedAt: string;
  cancellationReason: string | null;
  lines: OrderLine[];
  history: OrderStatusChange[];
  allowedTransitions: OrderStatus[];
}

export interface DashboardSummary {
  activeProducts: number;
  inactiveProducts: number;
  lowStockProducts: number;
  outOfStockProducts: number;
  totalOnHand: number;
  totalReserved: number;
  totalAvailable: number;
  totalOrders: number;
  ordersByStatus: { status: OrderStatus; count: number }[];
  ordersLast14Days: { date: string; count: number }[];
  fulfilledValueLast30Days: { currency: string; amount: number }[];
  lowStock: { productId: string; sku: string; productName: string; available: number; reorderThreshold: number }[];
  recentOrders: {
    id: string;
    number: string;
    customerName: string;
    status: OrderStatus;
    totalAmount: number;
    currency: string;
    createdAt: string;
  }[];
  generatedAt: string;
}

export interface ActivityEntry {
  id: number;
  eventType: string;
  module: 'catalog' | 'inventory' | 'orders';
  severity: ActivitySeverity;
  summary: string;
  subjectId: string | null;
  actor: string | null;
  occurredAt: string;
  recordedAt: string;
}

export interface User {
  id: string;
  email: string;
  displayName: string;
  role: Role;
  isActive: boolean;
  lockoutEnd: string | null;
  createdAt: string;
}

using InventoryOps.Application.Common.Caching;
using InventoryOps.Domain.Orders;

namespace InventoryOps.Application.Modules.Dashboard;

public sealed record OrderStatusCount(OrderStatus Status, int Count);

public sealed record CurrencyAmount(string Currency, decimal Amount);

public sealed record DailyOrderCount(DateOnly Date, int Count);

public sealed record LowStockItem(Guid ProductId, string Sku, string ProductName, int Available, int ReorderThreshold);

public sealed record RecentOrder(Guid Id, string Number, string CustomerName, OrderStatus Status, decimal TotalAmount, string Currency, DateTimeOffset CreatedAt);

/// <summary>
/// Operational metrics computed from SQL Server. "Fulfilled order value" is the sum of fulfilled
/// order totals per currency; it is not revenue (no payments, taxes, returns or discounts).
/// </summary>
public sealed record DashboardSummary(
    int ActiveProducts,
    int InactiveProducts,
    int LowStockProducts,
    int OutOfStockProducts,
    int TotalOnHand,
    int TotalReserved,
    int TotalAvailable,
    int TotalOrders,
    IReadOnlyList<OrderStatusCount> OrdersByStatus,
    IReadOnlyList<DailyOrderCount> OrdersLast14Days,
    IReadOnlyList<CurrencyAmount> FulfilledValueLast30Days,
    IReadOnlyList<LowStockItem> LowStock,
    IReadOnlyList<RecentOrder> RecentOrders,
    DateTimeOffset GeneratedAt);

public interface IDashboardReadStore
{
    Task<DashboardSummary> GetSummaryAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Cached for a short TTL and explicitly invalidated after stock and order writes, so the
/// dashboard is at most a few seconds behind under normal operation and never authoritative.
/// </summary>
public sealed class DashboardQueries(IDashboardReadStore readStore, ICacheStore cache)
{
    public async Task<DashboardSummary> GetSummaryAsync(CancellationToken cancellationToken) =>
        (await cache.GetOrCreateAsync<DashboardSummary>(
            CacheKeys.DashboardSummary, CacheKeys.DashboardTtl, async ct => await readStore.GetSummaryAsync(ct), cancellationToken))!;
}

using InventoryOps.Application.Common;
using InventoryOps.Application.Modules.Activity;
using InventoryOps.Application.Modules.Dashboard;
using InventoryOps.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace InventoryOps.Infrastructure.Persistence.ReadStores;

internal sealed class DashboardReadStore(InventoryOpsDbContext db, TimeProvider clock) : IDashboardReadStore
{
    private const int ListSize = 6;

    public async Task<DashboardSummary> GetSummaryAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        var productCounts = await db.Products.AsNoTracking()
            .GroupBy(p => p.IsActive)
            .Select(g => new { IsActive = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var activeStock = db.InventoryItems.AsNoTracking()
            .Join(db.Products.Where(p => p.IsActive), i => i.ProductId, p => p.Id, (i, p) => i);

        var stock = await activeStock
            .GroupBy(_ => 1)
            .Select(g => new
            {
                OnHand = g.Sum(i => i.OnHand),
                Reserved = g.Sum(i => i.Reserved),
                Low = g.Count(i => i.OnHand - i.Reserved <= i.ReorderThreshold),
                Out = g.Count(i => i.OnHand - i.Reserved == 0),
            })
            .SingleOrDefaultAsync(cancellationToken);

        var byStatus = await db.Orders.AsNoTracking()
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var since14 = new DateTimeOffset(now.UtcDateTime.Date.AddDays(-13), TimeSpan.Zero);
        var recentCreated = await db.Orders.AsNoTracking()
            .Where(o => o.CreatedAt >= since14)
            .Select(o => o.CreatedAt)
            .ToListAsync(cancellationToken);
        var perDay = Enumerable.Range(0, 14)
            .Select(d => DateOnly.FromDateTime(since14.UtcDateTime.AddDays(d)))
            .Select(day => new DailyOrderCount(day, recentCreated.Count(c => DateOnly.FromDateTime(c.UtcDateTime) == day)))
            .ToList();

        var since30 = now.AddDays(-30);
        var fulfilledValue = await db.Orders.AsNoTracking()
            .Where(o => o.Status == OrderStatus.Fulfilled && o.UpdatedAt >= since30)
            .GroupBy(o => o.Currency)
            .Select(g => new CurrencyAmount(g.Key, g.Sum(o => o.TotalAmount)))
            .ToListAsync(cancellationToken);

        var lowStock = await (
                from i in activeStock
                join p in db.Products on i.ProductId equals p.Id
                where i.OnHand - i.Reserved <= i.ReorderThreshold
                orderby i.OnHand - i.Reserved, p.Name
                select new LowStockItem(p.Id, p.Sku, p.Name, i.OnHand - i.Reserved, i.ReorderThreshold))
            .Take(ListSize)
            .ToListAsync(cancellationToken);

        var recentOrders = (await db.Orders.AsNoTracking()
                .OrderByDescending(o => o.Number)
                .Take(ListSize)
                .Select(o => new { o.Id, o.Number, o.CustomerName, o.Status, o.TotalAmount, o.Currency, o.CreatedAt })
                .ToListAsync(cancellationToken))
            .Select(o => new RecentOrder(o.Id, Order.FormatNumber(o.Number), o.CustomerName, o.Status, o.TotalAmount, o.Currency, o.CreatedAt))
            .ToList();

        return new DashboardSummary(
            ActiveProducts: productCounts.FirstOrDefault(c => c.IsActive)?.Count ?? 0,
            InactiveProducts: productCounts.FirstOrDefault(c => !c.IsActive)?.Count ?? 0,
            LowStockProducts: stock?.Low ?? 0,
            OutOfStockProducts: stock?.Out ?? 0,
            TotalOnHand: stock?.OnHand ?? 0,
            TotalReserved: stock?.Reserved ?? 0,
            TotalAvailable: (stock?.OnHand ?? 0) - (stock?.Reserved ?? 0),
            TotalOrders: byStatus.Sum(s => s.Count),
            OrdersByStatus: Enum.GetValues<OrderStatus>()
                .Select(s => new OrderStatusCount(s, byStatus.FirstOrDefault(b => b.Status == s)?.Count ?? 0))
                .ToList(),
            OrdersLast14Days: perDay,
            FulfilledValueLast30Days: fulfilledValue,
            LowStock: lowStock,
            RecentOrders: recentOrders,
            GeneratedAt: now);
    }
}

internal sealed class ActivityReadStore(InventoryOpsDbContext db) : IActivityReadStore
{
    public async Task<PagedResult<ActivityEntryDto>> ListAsync(ActivityQuery query, CancellationToken cancellationToken)
    {
        var entries = db.ActivityEntries.AsNoTracking();
        if (query.Module is { } module)
        {
            entries = entries.Where(a => a.Module == module);
        }

        if (query.Severity is { } severity)
        {
            entries = entries.Where(a => a.Severity == severity);
        }

        var total = await entries.CountAsync(cancellationToken);
        var items = await entries
            .OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(a => new ActivityEntryDto(a.Id, a.EventType, a.Module, a.Severity, a.Summary, a.SubjectId, a.Actor, a.OccurredAt, a.RecordedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<ActivityEntryDto>(items, query.Page, query.PageSize, total);
    }
}

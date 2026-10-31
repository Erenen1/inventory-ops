using InventoryOps.Application.Common;
using InventoryOps.Application.Modules.Orders;
using InventoryOps.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace InventoryOps.Infrastructure.Persistence.ReadStores;

internal sealed class OrderReadStore(InventoryOpsDbContext db) : IOrderReadStore
{
    public async Task<PagedResult<OrderListItem>> ListAsync(OrderListQuery query, CancellationToken cancellationToken)
    {
        var orders = db.Orders.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            var number = TryParseOrderNumber(term);
            orders = orders.Where(o => o.CustomerName.Contains(term) || (number != null && o.Number == number));
        }

        if (query.Status is { } status)
        {
            orders = orders.Where(o => o.Status == status);
        }

        if (query.From is { } from)
        {
            orders = orders.Where(o => o.CreatedAt >= from);
        }

        if (query.To is { } to)
        {
            orders = orders.Where(o => o.CreatedAt <= to);
        }

        var desc = query.SortDirection == SortDirection.Desc;
        var ordered = query.SortBy.ToUpperInvariant() switch
        {
            "NUMBER" => desc ? orders.OrderByDescending(o => o.Number) : orders.OrderBy(o => o.Number),
            "CUSTOMER" => desc ? orders.OrderByDescending(o => o.CustomerName) : orders.OrderBy(o => o.CustomerName),
            "TOTAL" => desc ? orders.OrderByDescending(o => o.TotalAmount) : orders.OrderBy(o => o.TotalAmount),
            "STATUS" => desc ? orders.OrderByDescending(o => o.Status) : orders.OrderBy(o => o.Status),
            _ => desc ? orders.OrderByDescending(o => o.CreatedAt) : orders.OrderBy(o => o.CreatedAt),
        };

        var total = await orders.CountAsync(cancellationToken);
        var rows = await ordered
            .ThenByDescending(o => o.Number)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(o => new
            {
                o.Id,
                o.Number,
                o.CustomerName,
                o.Status,
                LineCount = o.Lines.Count,
                TotalQuantity = o.Lines.Sum(l => l.Quantity),
                o.TotalAmount,
                o.Currency,
                o.CreatedBy,
                o.CreatedAt,
                o.UpdatedAt,
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(o => new OrderListItem(
                o.Id, Order.FormatNumber(o.Number), o.CustomerName, o.Status, o.LineCount, o.TotalQuantity, o.TotalAmount,
                o.Currency, o.CreatedBy, o.CreatedAt, o.UpdatedAt))
            .ToList();

        return new PagedResult<OrderListItem>(items, query.Page, query.PageSize, total);
    }

    public async Task<OrderDetails?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var order = await db.Orders.AsNoTracking()
            .Where(o => o.Id == id)
            .Select(o => new
            {
                o.Id,
                o.Number,
                o.CustomerName,
                o.Notes,
                o.Status,
                o.TotalAmount,
                o.Currency,
                o.CreatedBy,
                o.CreatedAt,
                o.UpdatedAt,
                o.CancellationReason,
                Lines = o.Lines.OrderBy(l => l.Sku)
                    .Select(l => new OrderLineDto(l.Id, l.ProductId, l.Sku, l.ProductName, l.UnitPrice, l.Quantity, l.LineTotal))
                    .ToList(),
                History = o.StatusHistory.OrderBy(h => h.ChangedAt).ThenBy(h => h.Id)
                    .Select(h => new OrderStatusChangeDto(h.FromStatus, h.ToStatus, h.ChangedBy, h.Reason, h.ChangedAt))
                    .ToList(),
            })
            .AsSplitQuery()
            .SingleOrDefaultAsync(cancellationToken);

        return order is null
            ? null
            : new OrderDetails(
                order.Id, Order.FormatNumber(order.Number), order.CustomerName, order.Notes, order.Status, order.TotalAmount,
                order.Currency, order.CreatedBy, order.CreatedAt, order.UpdatedAt, order.CancellationReason, order.Lines,
                order.History, Order.AllowedTransitionsFrom(order.Status));
    }

    /// <summary>Accepts "SO-100123", "so100123" or "100123".</summary>
    private static long? TryParseOrderNumber(string term)
    {
        var digits = term.StartsWith("SO", StringComparison.OrdinalIgnoreCase) ? term[2..].TrimStart('-') : term;
        return long.TryParse(digits, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n)
            ? n
            : null;
    }
}

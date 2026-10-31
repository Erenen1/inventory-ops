using InventoryOps.Application.Common;
using InventoryOps.Application.Modules.Inventory;
using Microsoft.EntityFrameworkCore;

namespace InventoryOps.Infrastructure.Persistence.ReadStores;

internal sealed class InventoryReadStore(InventoryOpsDbContext db) : IInventoryReadStore
{
    public async Task<PagedResult<InventoryItemDto>> ListAsync(InventoryListQuery query, CancellationToken cancellationToken)
    {
        var rows = Projection();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            rows = rows.Where(r => r.ProductName.Contains(term) || r.Sku.Contains(term));
        }

        if (query.LowStockOnly)
        {
            rows = rows.Where(r => r.IsLowStock);
        }

        var desc = query.SortDirection == SortDirection.Desc;
        var ordered = query.SortBy.ToUpperInvariant() switch
        {
            "SKU" => desc ? rows.OrderByDescending(r => r.Sku) : rows.OrderBy(r => r.Sku),
            "ONHAND" => desc ? rows.OrderByDescending(r => r.OnHand) : rows.OrderBy(r => r.OnHand),
            "AVAILABLE" => desc ? rows.OrderByDescending(r => r.Available) : rows.OrderBy(r => r.Available),
            "UPDATEDAT" => desc ? rows.OrderByDescending(r => r.UpdatedAt) : rows.OrderBy(r => r.UpdatedAt),
            _ => desc ? rows.OrderByDescending(r => r.ProductName) : rows.OrderBy(r => r.ProductName),
        };

        var total = await rows.CountAsync(cancellationToken);
        var items = await ordered
            .ThenBy(r => r.ProductId)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(ToDto)
            .ToListAsync(cancellationToken);

        return new PagedResult<InventoryItemDto>(items, query.Page, query.PageSize, total);
    }

    public Task<InventoryItemDto?> GetAsync(Guid productId, CancellationToken cancellationToken) =>
        Projection().Where(r => r.ProductId == productId).Select(ToDto).SingleOrDefaultAsync(cancellationToken);

    public async Task<PagedResult<StockMovementDto>> ListMovementsAsync(Guid productId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var movements = db.StockMovements.AsNoTracking().Where(m => m.ProductId == productId);
        var total = await movements.CountAsync(cancellationToken);

        var rows = await (
                from m in movements
                join o in db.Orders on m.OrderId equals o.Id into orders
                from o in orders.DefaultIfEmpty()
                orderby m.OccurredAt descending, m.Id descending
                select new
                {
                    m.Id, m.Type, m.OnHandChange, m.ReservedChange, m.OnHandAfter, m.ReservedAfter, m.Reason, m.Note, m.OrderId,
                    OrderNumber = (long?)o.Number, m.PerformedBy, m.OccurredAt,
                })
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = rows.Select(m => new StockMovementDto(
                m.Id, m.Type.ToString(), m.OnHandChange, m.ReservedChange, m.OnHandAfter, m.ReservedAfter, m.Reason?.ToString(), m.Note,
                m.OrderId, m.OrderNumber is { } n ? Domain.Orders.Order.FormatNumber(n) : null, m.PerformedBy, m.OccurredAt))
            .ToList();

        return new PagedResult<StockMovementDto>(items, page, pageSize, total);
    }

    private static readonly System.Linq.Expressions.Expression<Func<InventoryRow, InventoryItemDto>> ToDto = r => new InventoryItemDto(
        r.ProductId, r.Sku, r.ProductName, r.CategoryName, r.IsActive, r.OnHand, r.Reserved, r.Available, r.ReorderThreshold,
        r.IsLowStock, r.UpdatedAt);

    // Member-initialised row so EF Core can translate filters and sorting on projected members.
    private IQueryable<InventoryRow> Projection() =>
        from i in db.InventoryItems.AsNoTracking()
        join p in db.Products on i.ProductId equals p.Id
        join c in db.Categories on p.CategoryId equals c.Id
        select new InventoryRow
        {
            ProductId = i.ProductId,
            Sku = p.Sku,
            ProductName = p.Name,
            CategoryName = c.Name,
            IsActive = p.IsActive,
            OnHand = i.OnHand,
            Reserved = i.Reserved,
            Available = i.OnHand - i.Reserved,
            ReorderThreshold = i.ReorderThreshold,
            IsLowStock = i.OnHand - i.Reserved <= i.ReorderThreshold,
            UpdatedAt = i.UpdatedAt,
        };

    private sealed class InventoryRow
    {
        public Guid ProductId { get; init; }
        public string Sku { get; init; } = null!;
        public string ProductName { get; init; } = null!;
        public string CategoryName { get; init; } = null!;
        public bool IsActive { get; init; }
        public int OnHand { get; init; }
        public int Reserved { get; init; }
        public int Available { get; init; }
        public int ReorderThreshold { get; init; }
        public bool IsLowStock { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
    }
}

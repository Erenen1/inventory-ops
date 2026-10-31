using InventoryOps.Application.Common;
using InventoryOps.Domain.Inventory;

namespace InventoryOps.Application.Modules.Inventory;

public interface IInventoryRepository
{
    Task<InventoryItem?> GetAsync(Guid productId, CancellationToken cancellationToken);

    /// <summary>Loads stock positions for the given products, ordered by product id.</summary>
    Task<IReadOnlyList<InventoryItem>> GetManyAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken);

    void Add(InventoryItem item);
}

public sealed record InventoryListQuery(
    string? Search,
    bool LowStockOnly = false,
    string SortBy = InventorySortFields.Name,
    SortDirection SortDirection = SortDirection.Asc,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize);

public static class InventorySortFields
{
    public const string Name = "name";
    public const string Sku = "sku";
    public const string OnHand = "onHand";
    public const string Available = "available";
    public const string UpdatedAt = "updatedAt";

    public static readonly IReadOnlySet<string> Allowed =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Name, Sku, OnHand, Available, UpdatedAt };
}

public sealed record InventoryItemDto(
    Guid ProductId, string Sku, string ProductName, string CategoryName, bool IsActive, int OnHand, int Reserved,
    int Available, int ReorderThreshold, bool IsLowStock, DateTimeOffset UpdatedAt);

public sealed record StockMovementDto(
    long Id, string Type, int OnHandChange, int ReservedChange, int OnHandAfter, int ReservedAfter, string? Reason,
    string? Note, Guid? OrderId, string? OrderNumber, string PerformedBy, DateTimeOffset OccurredAt);

public interface IInventoryReadStore
{
    Task<PagedResult<InventoryItemDto>> ListAsync(InventoryListQuery query, CancellationToken cancellationToken);

    Task<InventoryItemDto?> GetAsync(Guid productId, CancellationToken cancellationToken);

    Task<PagedResult<StockMovementDto>> ListMovementsAsync(Guid productId, int page, int pageSize, CancellationToken cancellationToken);
}

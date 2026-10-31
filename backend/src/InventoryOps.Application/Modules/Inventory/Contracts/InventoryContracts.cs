namespace InventoryOps.Application.Modules.Inventory.Contracts;

public sealed record StockLine(Guid ProductId, int Quantity);

/// <summary>
/// Synchronous stock operations used by the Orders module. They run inside the caller's unit of
/// work, so the reservation and the order change commit (or roll back) together.
/// </summary>
public interface IStockReservations
{
    Task ReserveAsync(Guid orderId, IReadOnlyCollection<StockLine> lines, string actor, DateTimeOffset now, CancellationToken cancellationToken);

    Task ReleaseAsync(Guid orderId, IReadOnlyCollection<StockLine> lines, string actor, DateTimeOffset now, CancellationToken cancellationToken);

    Task FulfillAsync(Guid orderId, IReadOnlyCollection<StockLine> lines, string actor, DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>Used by the Catalog module to start tracking stock for a newly created product.</summary>
public interface IInventoryProvisioning
{
    void Provision(Guid productId, int reorderThreshold, DateTimeOffset now);
}

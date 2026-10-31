using InventoryOps.Application.Modules.Inventory.Contracts;
using InventoryOps.Domain.Common;
using InventoryOps.Domain.Inventory;

namespace InventoryOps.Application.Modules.Inventory;

/// <summary>
/// Implements the Inventory module's public contracts on top of the <see cref="InventoryItem"/>
/// aggregate. Concurrency safety comes from the aggregate's row version: if another transaction
/// changed the same stock row first, the commit fails and the unit of work retries the whole
/// operation against fresh data, so a reservation can never be based on stale availability.
/// </summary>
internal sealed class InventoryModule(IInventoryRepository repository) : IStockReservations, IInventoryProvisioning
{
    public void Provision(Guid productId, int reorderThreshold, DateTimeOffset now) =>
        repository.Add(InventoryItem.CreateFor(productId, reorderThreshold, now));

    public Task ReserveAsync(Guid orderId, IReadOnlyCollection<StockLine> lines, string actor, DateTimeOffset now, CancellationToken cancellationToken) =>
        ApplyAsync(lines, (item, qty) => item.Reserve(qty, orderId, actor, now), cancellationToken);

    public Task ReleaseAsync(Guid orderId, IReadOnlyCollection<StockLine> lines, string actor, DateTimeOffset now, CancellationToken cancellationToken) =>
        ApplyAsync(lines, (item, qty) => item.ReleaseReservation(qty, orderId, actor, now), cancellationToken);

    public Task FulfillAsync(Guid orderId, IReadOnlyCollection<StockLine> lines, string actor, DateTimeOffset now, CancellationToken cancellationToken) =>
        ApplyAsync(lines, (item, qty) => item.FulfillReservation(qty, orderId, actor, now), cancellationToken);

    private async Task ApplyAsync(IReadOnlyCollection<StockLine> lines, Action<InventoryItem, int> apply, CancellationToken cancellationToken)
    {
        var items = await repository.GetManyAsync(lines.Select(l => l.ProductId).ToList(), cancellationToken);
        var byProduct = items.ToDictionary(i => i.ProductId);

        // Deterministic order keeps lock acquisition consistent across concurrent transactions.
        foreach (var line in lines.OrderBy(l => l.ProductId))
        {
            if (!byProduct.TryGetValue(line.ProductId, out var item))
            {
                throw new DomainException("inventory.not_tracked", $"Product {line.ProductId} has no inventory record.");
            }

            apply(item, line.Quantity);
        }
    }
}

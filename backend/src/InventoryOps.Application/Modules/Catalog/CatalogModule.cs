using InventoryOps.Application.Modules.Catalog.Contracts;

namespace InventoryOps.Application.Modules.Catalog;

internal sealed class CatalogModule(IProductReadStore readStore) : ICatalogModule
{
    public async Task<IReadOnlyDictionary<Guid, CatalogProductSnapshot>> GetProductsAsync(
        IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken)
    {
        var snapshots = await readStore.GetSnapshotsAsync(productIds, cancellationToken);
        return snapshots.ToDictionary(p => p.ProductId);
    }
}

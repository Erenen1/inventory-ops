namespace InventoryOps.Application.Modules.Catalog.Contracts;

/// <summary>Public, read-only view of a product that other modules may depend on.</summary>
public sealed record CatalogProductSnapshot(Guid ProductId, string Sku, string Name, decimal UnitPrice, string Currency, bool IsActive);

/// <summary>
/// The Catalog module's public contract. Other modules use this instead of touching catalog
/// entities, which keeps the dependency direction explicit and testable.
/// </summary>
public interface ICatalogModule
{
    Task<IReadOnlyDictionary<Guid, CatalogProductSnapshot>> GetProductsAsync(
        IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken);
}

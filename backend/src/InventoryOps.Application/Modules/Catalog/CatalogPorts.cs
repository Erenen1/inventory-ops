using InventoryOps.Application.Common;
using InventoryOps.Application.Modules.Catalog.Contracts;
using InventoryOps.Domain.Catalog;

namespace InventoryOps.Application.Modules.Catalog;

public interface IProductRepository
{
    Task<Product?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Product>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    Task<bool> SkuExistsAsync(Sku sku, CancellationToken cancellationToken);

    void Add(Product product);
}

public interface ICategoryRepository
{
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken);

    void Add(Category category);
}

public sealed record ProductListQuery(
    string? Search,
    Guid? CategoryId,
    bool? IsActive,
    string SortBy = ProductSortFields.Name,
    SortDirection SortDirection = SortDirection.Asc,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize);

public static class ProductSortFields
{
    public const string Name = "name";
    public const string Sku = "sku";
    public const string Price = "price";
    public const string Available = "available";
    public const string UpdatedAt = "updatedAt";

    public static readonly IReadOnlySet<string> Allowed =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Name, Sku, Price, Available, UpdatedAt };
}

public sealed record ProductListItem(
    Guid Id, string Sku, string Name, Guid CategoryId, string CategoryName, decimal UnitPrice, string Currency,
    bool IsActive, int OnHand, int Reserved, int Available, DateTimeOffset UpdatedAt);

public sealed record ProductDetails(
    Guid Id, string Sku, string Name, string? Description, Guid CategoryId, string CategoryName,
    decimal UnitPrice, string Currency, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record CategoryDto(Guid Id, string Name, int ProductCount);

/// <summary>Projection-based read access for catalog screens (no tracking, no domain objects).</summary>
public interface IProductReadStore
{
    Task<PagedResult<ProductListItem>> ListAsync(ProductListQuery query, CancellationToken cancellationToken);

    Task<ProductDetails?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<CategoryDto>> ListCategoriesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<CatalogProductSnapshot>> GetSnapshotsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}

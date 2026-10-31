using InventoryOps.Application.Common;
using InventoryOps.Application.Modules.Catalog;
using InventoryOps.Application.Modules.Catalog.Contracts;
using Microsoft.EntityFrameworkCore;

namespace InventoryOps.Infrastructure.Persistence.ReadStores;

internal sealed class CatalogReadStore(InventoryOpsDbContext db) : IProductReadStore
{
    public async Task<PagedResult<ProductListItem>> ListAsync(ProductListQuery query, CancellationToken cancellationToken)
    {
        var rows =
            from p in db.Products.AsNoTracking()
            join c in db.Categories on p.CategoryId equals c.Id
            join i in db.InventoryItems on p.Id equals i.ProductId
            select new { Product = p, CategoryName = c.Name, i.OnHand, i.Reserved, Available = i.OnHand - i.Reserved };

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            rows = rows.Where(r => r.Product.Name.Contains(term) || r.Product.Sku.Contains(term));
        }

        if (query.CategoryId is { } categoryId)
        {
            rows = rows.Where(r => r.Product.CategoryId == categoryId);
        }

        if (query.IsActive is { } isActive)
        {
            rows = rows.Where(r => r.Product.IsActive == isActive);
        }

        var desc = query.SortDirection == SortDirection.Desc;
        var ordered = query.SortBy.ToUpperInvariant() switch
        {
            "SKU" => desc ? rows.OrderByDescending(r => r.Product.Sku) : rows.OrderBy(r => r.Product.Sku),
            "PRICE" => desc ? rows.OrderByDescending(r => r.Product.Price.Amount) : rows.OrderBy(r => r.Product.Price.Amount),
            "AVAILABLE" => desc ? rows.OrderByDescending(r => r.Available) : rows.OrderBy(r => r.Available),
            "UPDATEDAT" => desc ? rows.OrderByDescending(r => r.Product.UpdatedAt) : rows.OrderBy(r => r.Product.UpdatedAt),
            _ => desc ? rows.OrderByDescending(r => r.Product.Name) : rows.OrderBy(r => r.Product.Name),
        };

        var total = await rows.CountAsync(cancellationToken);
        var items = await ordered
            .ThenBy(r => r.Product.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(r => new ProductListItem(
                r.Product.Id, r.Product.Sku, r.Product.Name, r.Product.CategoryId, r.CategoryName, r.Product.Price.Amount,
                r.Product.Price.Currency, r.Product.IsActive, r.OnHand, r.Reserved, r.Available, r.Product.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<ProductListItem>(items, query.Page, query.PageSize, total);
    }

    public Task<ProductDetails?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        (from p in db.Products.AsNoTracking()
         join c in db.Categories on p.CategoryId equals c.Id
         where p.Id == id
         select new ProductDetails(
             p.Id, p.Sku, p.Name, p.Description, p.CategoryId, c.Name, p.Price.Amount, p.Price.Currency, p.IsActive, p.CreatedAt, p.UpdatedAt))
        .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<CategoryDto>> ListCategoriesAsync(CancellationToken cancellationToken) =>
        await db.Categories.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, db.Products.Count(p => p.CategoryId == c.Id)))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CatalogProductSnapshot>> GetSnapshotsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await db.Products.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new CatalogProductSnapshot(p.Id, p.Sku, p.Name, p.Price.Amount, p.Price.Currency, p.IsActive))
            .ToListAsync(cancellationToken);
}

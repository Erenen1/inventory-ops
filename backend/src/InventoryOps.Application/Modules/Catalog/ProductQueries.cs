using FluentValidation;
using InventoryOps.Application.Common;
using InventoryOps.Application.Common.Caching;

namespace InventoryOps.Application.Modules.Catalog;

public sealed class ProductListQueryValidator : AbstractValidator<ProductListQuery>
{
    public ProductListQueryValidator()
    {
        RuleFor(x => x.Search).MaximumLength(Paging.MaxSearchLength);
        RuleFor(x => x.SortBy).Must(ProductSortFields.Allowed.Contains)
            .WithMessage($"sortBy must be one of: {string.Join(", ", ProductSortFields.Allowed)}.");
        RuleFor(x => x.SortDirection).IsInEnum();
        this.ApplyPagingRules(x => x.Page, x => x.PageSize);
    }
}

/// <summary>
/// Catalog reads. Product lists are not cached: they combine search, filters, sorting and live stock
/// figures, so the hit ratio would be low and stale availability would mislead order entry.
/// Product details and categories are cached (cache-aside) and invalidated after writes.
/// </summary>
public sealed class ProductQueries(IProductReadStore readStore, ICacheStore cache, IValidator<ProductListQuery> validator)
{
    public async Task<PagedResult<ProductListItem>> ListAsync(ProductListQuery query, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(query, cancellationToken);
        return await readStore.ListAsync(query, cancellationToken);
    }

    public async Task<ProductDetails> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync(CacheKeys.Product(id), CacheKeys.ProductTtl, ct => readStore.GetAsync(id, ct), cancellationToken)
        ?? throw new NotFoundException("Product", id);

    public async Task<IReadOnlyList<CategoryDto>> ListCategoriesAsync(CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync<CategoryListCacheEntry>(
            CacheKeys.Categories,
            CacheKeys.CategoriesTtl,
            async ct => new CategoryListCacheEntry(await readStore.ListCategoriesAsync(ct)),
            cancellationToken) is { } entry ? entry.Items : [];

    // Wrapper so the cached value is a reference type with a stable JSON shape.
    internal sealed record CategoryListCacheEntry(IReadOnlyList<CategoryDto> Items);
}

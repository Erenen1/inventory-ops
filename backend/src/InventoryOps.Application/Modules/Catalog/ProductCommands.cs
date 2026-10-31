using FluentValidation;
using InventoryOps.Application.Common;
using InventoryOps.Application.Common.Caching;
using InventoryOps.Application.Common.Persistence;
using InventoryOps.Application.Modules.Inventory.Contracts;
using InventoryOps.Domain.Catalog;
using InventoryOps.Domain.Common;
using InventoryOps.Domain.Inventory;

namespace InventoryOps.Application.Modules.Catalog;

public sealed record CreateProductCommand(
    string Sku, string Name, string? Description, Guid CategoryId, decimal UnitPrice, string Currency, int ReorderThreshold = 10);

public sealed record UpdateProductCommand(string Name, string? Description, Guid CategoryId, decimal UnitPrice, string Currency);

internal static class ProductRules
{
    public static void ApplyDetailRules<T>(
        this AbstractValidator<T> v, Func<T, string> name, Func<T, string?> description, Func<T, Guid> categoryId,
        Func<T, decimal> unitPrice, Func<T, string> currency)
    {
        v.RuleFor(x => name(x)).NotEmpty().MaximumLength(Product.NameMaxLength).OverridePropertyName("name");
        v.RuleFor(x => description(x)).MaximumLength(Product.DescriptionMaxLength).OverridePropertyName("description");
        v.RuleFor(x => categoryId(x)).NotEmpty().OverridePropertyName("categoryId");
        v.RuleFor(x => unitPrice(x)).InclusiveBetween(0m, Money.MaxAmount).PrecisionScale(12, 2, ignoreTrailingZeros: true)
            .OverridePropertyName("unitPrice");
        v.RuleFor(x => currency(x)).NotEmpty().Must(c => Money.SupportedCurrencies.Contains(c))
            .WithMessage($"currency must be one of: {string.Join(", ", Money.SupportedCurrencies)}.").OverridePropertyName("currency");
    }
}

public sealed class CreateProductValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(Sku.MaxLength).Matches("^[A-Za-z0-9][A-Za-z0-9-]{1,30}[A-Za-z0-9]$")
            .WithMessage("SKU must be 3-32 letters, digits or dashes and start/end with a letter or digit.");
        this.ApplyDetailRules(x => x.Name, x => x.Description, x => x.CategoryId, x => x.UnitPrice, x => x.Currency);
        RuleFor(x => x.ReorderThreshold).InclusiveBetween(0, InventoryItem.MaxQuantity);
    }
}

public sealed class UpdateProductValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductValidator() =>
        this.ApplyDetailRules(x => x.Name, x => x.Description, x => x.CategoryId, x => x.UnitPrice, x => x.Currency);
}

public sealed class CreateProductHandler(
    IProductRepository products,
    ICategoryRepository categories,
    IInventoryProvisioning inventory,
    IUnitOfWork unitOfWork,
    ICacheStore cache,
    IValidator<CreateProductCommand> validator,
    TimeProvider clock)
{
    public async Task<Guid> HandleAsync(CreateProductCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);
        var sku = Sku.Create(command.Sku);

        var productId = await unitOfWork.ExecuteAsync(
            async ct =>
            {
                if (!await categories.ExistsAsync(command.CategoryId, ct))
                {
                    throw new DomainException("catalog.category_not_found", "The selected category does not exist.");
                }

                // Fast, friendly check; the unique index on SKU is the real guarantee under concurrency.
                if (await products.SkuExistsAsync(sku, ct))
                {
                    throw new ConflictException("catalog.duplicate_sku", $"A product with SKU '{sku}' already exists.");
                }

                var now = clock.GetUtcNow();
                var product = Product.Create(
                    sku, command.Name, command.Description, command.CategoryId, new Money(command.UnitPrice, command.Currency), now);
                products.Add(product);
                inventory.Provision(product.Id, command.ReorderThreshold, now);
                return product.Id;
            },
            cancellationToken);

        await cache.RemoveAsync(cancellationToken, CacheKeys.Categories, CacheKeys.DashboardSummary);
        return productId;
    }
}

public sealed class UpdateProductHandler(
    IProductRepository products,
    ICategoryRepository categories,
    IUnitOfWork unitOfWork,
    ICacheStore cache,
    IValidator<UpdateProductCommand> validator,
    TimeProvider clock)
{
    public async Task HandleAsync(Guid productId, UpdateProductCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        await unitOfWork.ExecuteAsync(
            async ct =>
            {
                var product = await products.GetAsync(productId, ct) ?? throw new NotFoundException("Product", productId);
                if (product.CategoryId != command.CategoryId && !await categories.ExistsAsync(command.CategoryId, ct))
                {
                    throw new DomainException("catalog.category_not_found", "The selected category does not exist.");
                }

                product.UpdateDetails(
                    command.Name, command.Description, command.CategoryId, new Money(command.UnitPrice, command.Currency), clock.GetUtcNow());
            },
            cancellationToken);

        await cache.RemoveAsync(cancellationToken, CacheKeys.Product(productId), CacheKeys.Categories);
    }
}

public sealed class SetProductActiveHandler(IProductRepository products, IUnitOfWork unitOfWork, ICacheStore cache, TimeProvider clock)
{
    public async Task HandleAsync(Guid productId, bool isActive, CancellationToken cancellationToken)
    {
        await unitOfWork.ExecuteAsync(
            async ct =>
            {
                var product = await products.GetAsync(productId, ct) ?? throw new NotFoundException("Product", productId);
                if (isActive)
                {
                    product.Reactivate(clock.GetUtcNow());
                }
                else
                {
                    product.Deactivate(clock.GetUtcNow());
                }
            },
            cancellationToken);

        await cache.RemoveAsync(cancellationToken, CacheKeys.Product(productId), CacheKeys.DashboardSummary);
    }
}

public sealed record CreateCategoryCommand(string Name);

public sealed class CreateCategoryValidator : AbstractValidator<CreateCategoryCommand>
{
    public CreateCategoryValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(Category.NameMaxLength);
}

public sealed class CreateCategoryHandler(
    ICategoryRepository categories, IUnitOfWork unitOfWork, ICacheStore cache, IValidator<CreateCategoryCommand> validator, TimeProvider clock)
{
    public async Task<Guid> HandleAsync(CreateCategoryCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);
        var id = await unitOfWork.ExecuteAsync(
            async ct =>
            {
                var category = Category.Create(command.Name, clock.GetUtcNow());
                if (await categories.NameExistsAsync(category.Name, ct))
                {
                    throw new ConflictException("catalog.duplicate_category", $"Category '{category.Name}' already exists.");
                }

                categories.Add(category);
                return category.Id;
            },
            cancellationToken);

        await cache.RemoveAsync(cancellationToken, CacheKeys.Categories);
        return id;
    }
}

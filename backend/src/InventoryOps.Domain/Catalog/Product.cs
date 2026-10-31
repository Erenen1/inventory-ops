using InventoryOps.Domain.Common;

namespace InventoryOps.Domain.Catalog;

/// <summary>
/// A sellable catalog item. Products are never hard-deleted: order lines keep their own price and
/// name snapshots, and deactivation removes a product from new orders while preserving history.
/// </summary>
public sealed class Product : AggregateRoot
{
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 2000;

    public Guid Id { get; private set; }
    public string Sku { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public Guid CategoryId { get; private set; }
    public Money Price { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Product()
    {
    }

    public static Product Create(
        Sku sku, string name, string? description, Guid categoryId, Money price, DateTimeOffset now)
    {
        var product = new Product
        {
            Id = Guid.CreateVersion7(now),
            Sku = sku.Value,
            IsActive = true,
            CreatedAt = now,
        };
        product.Apply(name, description, categoryId, price, now);
        product.Raise(new ProductCreated(product.Id, product.Sku, product.Name, now));
        return product;
    }

    public void UpdateDetails(string name, string? description, Guid categoryId, Money price, DateTimeOffset now)
    {
        Apply(name, description, categoryId, price, now);
        Raise(new ProductUpdated(Id, Sku, Name, now));
    }

    public void Deactivate(DateTimeOffset now)
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        UpdatedAt = now;
        Raise(new ProductDeactivated(Id, Sku, Name, now));
    }

    public void Reactivate(DateTimeOffset now)
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        UpdatedAt = now;
        Raise(new ProductUpdated(Id, Sku, Name, now));
    }

    private void Apply(string name, string? description, Guid categoryId, Money price, DateTimeOffset now)
    {
        var trimmedName = name?.Trim() ?? string.Empty;
        if (trimmedName.Length is 0 or > NameMaxLength)
        {
            throw new DomainException("catalog.name_invalid", $"Product name must be 1-{NameMaxLength} characters.");
        }

        var trimmedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (trimmedDescription?.Length > DescriptionMaxLength)
        {
            throw new DomainException("catalog.description_invalid", $"Description cannot exceed {DescriptionMaxLength} characters.");
        }

        if (categoryId == Guid.Empty)
        {
            throw new DomainException("catalog.category_required", "A category is required.");
        }

        Name = trimmedName;
        Description = trimmedDescription;
        CategoryId = categoryId;
        Price = price;
        UpdatedAt = now;
    }
}

public sealed record ProductCreated(Guid ProductId, string Sku, string Name, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record ProductUpdated(Guid ProductId, string Sku, string Name, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record ProductDeactivated(Guid ProductId, string Sku, string Name, DateTimeOffset OccurredAt) : IDomainEvent;

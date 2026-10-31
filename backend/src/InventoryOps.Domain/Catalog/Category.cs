using InventoryOps.Domain.Common;

namespace InventoryOps.Domain.Catalog;

public sealed class Category
{
    public const int NameMaxLength = 100;

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }

    private Category()
    {
    }

    public static Category Create(string name, DateTimeOffset now)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > NameMaxLength)
        {
            throw new DomainException("catalog.category_name_invalid", $"Category name must be 1-{NameMaxLength} characters.");
        }

        return new Category { Id = Guid.CreateVersion7(now), Name = trimmed, CreatedAt = now };
    }
}

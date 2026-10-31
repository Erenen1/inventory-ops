using InventoryOps.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InventoryOps.Infrastructure.Persistence.Configurations;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories", Schemas.Catalog);
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Name).HasMaxLength(Category.NameMaxLength).IsRequired();
        builder.HasIndex(c => c.Name).IsUnique();
    }
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products", Schemas.Catalog, t =>
        {
            t.HasCheckConstraint("CK_Products_UnitPrice_NonNegative", "[UnitPrice] >= 0");
        });

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Sku).HasMaxLength(Sku.MaxLength).IsUnicode(false).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(Product.NameMaxLength).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(Product.DescriptionMaxLength);

        builder.ComplexProperty(p => p.Price, price =>
        {
            price.Property(m => m.Amount).HasColumnName("UnitPrice").HasPrecision(12, 2);
            price.Property(m => m.Currency).HasColumnName("Currency").HasMaxLength(3).IsUnicode(false).IsFixedLength();
        });

        // Optimistic concurrency token kept out of the domain model.
        builder.Property<byte[]>("RowVersion").IsRowVersion();

        builder.HasOne<Category>().WithMany().HasForeignKey(p => p.CategoryId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => p.Sku).IsUnique();
        builder.HasIndex(p => p.Name);
        builder.HasIndex(p => new { p.CategoryId, p.IsActive });
        builder.Ignore(p => p.DomainEvents);
    }
}

internal static class Schemas
{
    public const string Catalog = "catalog";
    public const string Inventory = "inventory";
    public const string Orders = "orders";
    public const string Activity = "activity";
    public const string Messaging = "messaging";
    public const string Identity = "identity";
}

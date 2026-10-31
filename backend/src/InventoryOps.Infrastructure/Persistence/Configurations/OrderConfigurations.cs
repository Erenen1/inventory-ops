using InventoryOps.Domain.Catalog;
using InventoryOps.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InventoryOps.Infrastructure.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public const string NumberSequence = "OrderNumbers";

    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders", Schemas.Orders, t =>
        {
            t.HasCheckConstraint("CK_Orders_TotalAmount_NonNegative", "[TotalAmount] >= 0");
        });

        // GUID v7 ids are not sequential under SQL Server's uniqueidentifier ordering, so the
        // clustered index is placed on the monotonically increasing order number instead.
        builder.HasKey(o => o.Id).IsClustered(false);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.HasIndex(o => o.Number).IsUnique().IsClustered();

        builder.Property(o => o.CustomerName).HasMaxLength(Order.CustomerNameMaxLength).IsRequired();
        builder.Property(o => o.Notes).HasMaxLength(Order.NotesMaxLength);
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.Property(o => o.Currency).HasMaxLength(3).IsUnicode(false).IsFixedLength();
        builder.Property(o => o.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(o => o.CancellationReason).HasMaxLength(Order.ReasonMaxLength);
        builder.Property(o => o.IdempotencyKey).HasMaxLength(64).IsRequired();
        builder.Property(o => o.RequestHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property<byte[]>("RowVersion").IsRowVersion();

        builder.HasMany(o => o.Lines).WithOne().HasForeignKey(l => l.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(o => o.StatusHistory).WithOne().HasForeignKey(h => h.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.StatusHistory).HasField("_statusHistory").UsePropertyAccessMode(PropertyAccessMode.Field);

        // Idempotency keys are scoped per user; the unique index makes concurrent replays safe.
        builder.HasIndex(o => new { o.CreatedBy, o.IdempotencyKey }).IsUnique();
        builder.HasIndex(o => new { o.Status, o.CreatedAt });
        builder.HasIndex(o => o.CreatedAt);

        builder.Ignore(o => o.DisplayNumber);
        builder.Ignore(o => o.DomainEvents);
    }
}

internal sealed class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> builder)
    {
        builder.ToTable("OrderLines", Schemas.Orders, t =>
        {
            t.HasCheckConstraint("CK_OrderLines_Quantity_Positive", "[Quantity] > 0");
            t.HasCheckConstraint("CK_OrderLines_UnitPrice_NonNegative", "[UnitPrice] >= 0");
        });

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.Sku).HasMaxLength(Sku.MaxLength).IsUnicode(false).IsRequired();
        builder.Property(l => l.ProductName).HasMaxLength(Product.NameMaxLength).IsRequired();
        builder.Property(l => l.UnitPrice).HasPrecision(12, 2);

        // Products are never deleted, but Restrict documents that history must not cascade away.
        builder.HasOne<Product>().WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(l => new { l.OrderId, l.ProductId }).IsUnique();
        builder.HasIndex(l => l.ProductId);
    }
}

internal sealed class OrderStatusChangeConfiguration : IEntityTypeConfiguration<OrderStatusChange>
{
    public void Configure(EntityTypeBuilder<OrderStatusChange> builder)
    {
        builder.ToTable("OrderStatusHistory", Schemas.Orders);
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).UseIdentityColumn();
        builder.Property(h => h.FromStatus).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.Property(h => h.ToStatus).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.Property(h => h.ChangedBy).HasMaxLength(256).IsRequired();
        builder.Property(h => h.Reason).HasMaxLength(Order.ReasonMaxLength);
        builder.HasIndex(h => h.OrderId);
    }
}

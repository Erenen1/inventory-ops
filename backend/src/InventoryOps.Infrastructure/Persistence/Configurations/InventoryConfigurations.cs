using InventoryOps.Domain.Catalog;
using InventoryOps.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InventoryOps.Infrastructure.Persistence.Configurations;

internal sealed class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> builder)
    {
        // Check constraints are the last line of defence for the stock invariants enforced by the aggregate.
        builder.ToTable("InventoryItems", Schemas.Inventory, t =>
        {
            t.HasCheckConstraint("CK_InventoryItems_OnHand_NonNegative", "[OnHand] >= 0");
            t.HasCheckConstraint("CK_InventoryItems_Reserved_NonNegative", "[Reserved] >= 0");
            t.HasCheckConstraint("CK_InventoryItems_Reserved_LTE_OnHand", "[Reserved] <= [OnHand]");
            t.HasCheckConstraint("CK_InventoryItems_ReorderThreshold_NonNegative", "[ReorderThreshold] >= 0");
        });

        builder.HasKey(i => i.ProductId);
        builder.Property(i => i.ProductId).ValueGeneratedNever();
        builder.Property<byte[]>("RowVersion").IsRowVersion();

        builder.HasOne<Product>().WithOne().HasForeignKey<InventoryItem>(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);

        // Write-only navigation: new movements are appended and inserted, history is never loaded here.
        builder.HasMany(i => i.PendingMovements).WithOne().HasForeignKey(m => m.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(i => i.PendingMovements).HasField("_movements").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(i => i.Available);
        builder.Ignore(i => i.IsLowStock);
        builder.Ignore(i => i.DomainEvents);
    }
}

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("StockMovements", Schemas.Inventory);
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).UseIdentityColumn();
        builder.Property(m => m.Type).HasConversion<string>().HasMaxLength(32).IsUnicode(false);
        builder.Property(m => m.Reason).HasConversion<string>().HasMaxLength(32).IsUnicode(false);
        builder.Property(m => m.Note).HasMaxLength(StockMovement.NoteMaxLength);
        builder.Property(m => m.PerformedBy).HasMaxLength(256).IsRequired();

        builder.HasIndex(m => new { m.ProductId, m.OccurredAt }).IsDescending(false, true);
        builder.HasIndex(m => m.OrderId).HasFilter("[OrderId] IS NOT NULL");
    }
}

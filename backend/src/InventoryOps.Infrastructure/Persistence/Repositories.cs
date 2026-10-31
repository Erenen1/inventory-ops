using InventoryOps.Application.Modules.Catalog;
using InventoryOps.Application.Modules.Inventory;
using InventoryOps.Application.Modules.Orders;
using InventoryOps.Domain.Catalog;
using InventoryOps.Domain.Inventory;
using InventoryOps.Domain.Orders;
using InventoryOps.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace InventoryOps.Infrastructure.Persistence;

internal sealed class ProductRepository(InventoryOpsDbContext db) : IProductRepository
{
    public Task<Product?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Products.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Product>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await db.Products.Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken);

    public Task<bool> SkuExistsAsync(Sku sku, CancellationToken cancellationToken) =>
        db.Products.AnyAsync(p => p.Sku == sku.Value, cancellationToken);

    public void Add(Product product) => db.Products.Add(product);
}

internal sealed class CategoryRepository(InventoryOpsDbContext db) : ICategoryRepository
{
    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
        db.Categories.AnyAsync(c => c.Id == id, cancellationToken);

    public Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken) =>
        db.Categories.AnyAsync(c => c.Name == name, cancellationToken);

    public void Add(Category category) => db.Categories.Add(category);
}

internal sealed class InventoryRepository(InventoryOpsDbContext db) : IInventoryRepository
{
    public Task<InventoryItem?> GetAsync(Guid productId, CancellationToken cancellationToken) =>
        db.InventoryItems.SingleOrDefaultAsync(i => i.ProductId == productId, cancellationToken);

    public async Task<IReadOnlyList<InventoryItem>> GetManyAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken) =>
        await db.InventoryItems.Where(i => productIds.Contains(i.ProductId)).OrderBy(i => i.ProductId).ToListAsync(cancellationToken);

    public void Add(InventoryItem item) => db.InventoryItems.Add(item);
}

internal sealed class OrderRepository(InventoryOpsDbContext db) : IOrderRepository
{
    public Task<Order?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Orders.Include(o => o.Lines).Include(o => o.StatusHistory).AsSplitQuery()
            .SingleOrDefaultAsync(o => o.Id == id, cancellationToken);

    public Task<Order?> FindByIdempotencyKeyAsync(string createdBy, string idempotencyKey, CancellationToken cancellationToken) =>
        db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.CreatedBy == createdBy && o.IdempotencyKey == idempotencyKey, cancellationToken);

    public void Add(Order order) => db.Orders.Add(order);
}

internal sealed class SqlOrderNumberGenerator(InventoryOpsDbContext db) : IOrderNumberGenerator
{
    private const string Sql = "SELECT NEXT VALUE FOR [" + Schemas.Orders + "].[" + OrderConfiguration.NumberSequence + "]";

    // Sequence values are not transactional: a rolled-back order leaves a gap, which is acceptable.
    // NEXT VALUE FOR cannot appear in a derived table, so the statement is executed uncomposed.
    public async Task<long> NextAsync(CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = Sql;
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }
}

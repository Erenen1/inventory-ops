using InventoryOps.Application.Modules.Orders;
using InventoryOps.Domain.Catalog;
using InventoryOps.Domain.Common;
using InventoryOps.Domain.Inventory;
using InventoryOps.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InventoryOps.Infrastructure.Persistence.Seeding;

/// <summary>
/// Deterministic demo dataset for local development. It goes through the domain model (so every
/// invariant, stock movement and outbox event is produced exactly as in real use) and is skipped
/// entirely when the catalog already contains products, so repeated runs never duplicate data.
/// </summary>
public sealed class DemoDataSeeder(
    InventoryOpsDbContext db, IOrderNumberGenerator orderNumbers, TimeProvider clock, ILogger<DemoDataSeeder> logger)
{
    private const string Actor = "seed";

    private static readonly (string Category, string Sku, string Name, decimal Price, int Stock, int Threshold, bool Active)[] Products =
    [
        ("Electronics", "ELC-USB-C-HUB", "USB-C Hub 7-in-1", 49.90m, 120, 25, true),
        ("Electronics", "ELC-MON-27Q", "27\" QHD Monitor", 289.00m, 18, 10, true),
        ("Electronics", "ELC-KB-MECH", "Mechanical Keyboard (TKL)", 119.00m, 42, 15, true),
        ("Electronics", "ELC-MOUSE-WL", "Wireless Mouse", 29.50m, 9, 20, true),
        ("Electronics", "ELC-HDST-ANC", "Noise-Cancelling Headset", 159.00m, 31, 10, true),
        ("Networking", "NET-SW-24G", "24-Port Gigabit Switch", 219.00m, 14, 5, true),
        ("Networking", "NET-AP-WIFI6", "Wi-Fi 6 Access Point", 139.00m, 26, 8, true),
        ("Networking", "NET-CAT6-3M", "Cat6 Patch Cable 3 m", 4.75m, 640, 150, true),
        ("Networking", "NET-SFP-10G", "10G SFP+ Transceiver", 39.00m, 6, 12, true),
        ("Networking", "NET-RTR-EDGE", "Edge Router", 329.00m, 7, 3, true),
        ("Office Supplies", "OFF-PAPER-A4", "A4 Copy Paper (500 sheets)", 6.20m, 480, 100, true),
        ("Office Supplies", "OFF-PEN-BLK12", "Black Gel Pens (12-pack)", 8.90m, 75, 30, true),
        ("Office Supplies", "OFF-NOTE-A5", "A5 Dotted Notebook", 5.40m, 22, 40, true),
        ("Office Supplies", "OFF-STAPLER", "Heavy-Duty Stapler", 24.00m, 35, 10, true),
        ("Office Supplies", "OFF-LBL-THERM", "Thermal Labels 100x150 (500)", 18.50m, 64, 20, true),
        ("Furniture", "FUR-CHAIR-ERG", "Ergonomic Office Chair", 349.00m, 12, 4, true),
        ("Furniture", "FUR-DESK-SIT", "Sit-Stand Desk 140 cm", 499.00m, 3, 3, true),
        ("Furniture", "FUR-CAB-3DR", "3-Drawer Filing Cabinet", 179.00m, 9, 3, true),
        ("Furniture", "FUR-LAMP-LED", "LED Desk Lamp", 45.00m, 0, 5, false),
        ("Packaging", "PKG-BOX-S", "Shipping Box Small (25)", 19.90m, 210, 50, true),
        ("Packaging", "PKG-BOX-M", "Shipping Box Medium (25)", 27.50m, 150, 50, true),
        ("Packaging", "PKG-TAPE-48", "Packing Tape 48 mm (6)", 12.00m, 38, 40, true),
        ("Packaging", "PKG-BUBBLE-50", "Bubble Wrap Roll 50 m", 22.00m, 44, 15, true),
        ("Packaging", "PKG-MAILER-A4", "Padded Mailer A4 (50)", 31.00m, 57, 20, true),
    ];

    private static readonly (string Customer, int DaysAgo, int Hour, OrderStatus Status, (string Sku, int Qty)[] Lines)[] Orders =
    [
        ("Northwind Traders", 13, 9, OrderStatus.Fulfilled, [("ELC-USB-C-HUB", 10), ("NET-CAT6-3M", 40)]),
        ("Contoso Ltd.", 12, 14, OrderStatus.Fulfilled, [("ELC-MON-27Q", 4), ("ELC-KB-MECH", 4), ("ELC-MOUSE-WL", 4)]),
        ("Fabrikam Inc.", 11, 10, OrderStatus.Cancelled, [("FUR-CHAIR-ERG", 6)]),
        ("Adventure Works", 10, 16, OrderStatus.Fulfilled, [("OFF-PAPER-A4", 60), ("OFF-PEN-BLK12", 10)]),
        ("Tailspin Toys", 9, 11, OrderStatus.Fulfilled, [("PKG-BOX-S", 30), ("PKG-BOX-M", 20), ("PKG-TAPE-48", 8)]),
        ("Wide World Importers", 8, 13, OrderStatus.Fulfilled, [("NET-SW-24G", 2), ("NET-AP-WIFI6", 4), ("NET-SFP-10G", 4)]),
        ("Litware Inc.", 6, 9, OrderStatus.Processing, [("FUR-DESK-SIT", 2), ("FUR-CHAIR-ERG", 2)]),
        ("Proseware LLC", 5, 15, OrderStatus.Cancelled, [("OFF-STAPLER", 5)]),
        ("Northwind Traders", 4, 10, OrderStatus.Processing, [("PKG-MAILER-A4", 12), ("PKG-BUBBLE-50", 6)]),
        ("Contoso Ltd.", 3, 12, OrderStatus.Processing, [("ELC-HDST-ANC", 8)]),
        ("Adventure Works", 2, 9, OrderStatus.Submitted, [("OFF-LBL-THERM", 10), ("OFF-NOTE-A5", 12)]),
        ("Fabrikam Inc.", 1, 14, OrderStatus.Submitted, [("NET-RTR-EDGE", 2), ("NET-CAT6-3M", 100)]),
        ("Tailspin Toys", 1, 16, OrderStatus.Submitted, [("ELC-USB-C-HUB", 15)]),
        ("Wide World Importers", 0, 8, OrderStatus.Submitted, [("FUR-CAB-3DR", 3), ("OFF-PAPER-A4", 40)]),
    ];

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (await db.Products.AnyAsync(cancellationToken))
        {
            logger.LogInformation("Catalog already contains products; demo data seeding skipped");
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var today = new DateTimeOffset(clock.GetUtcNow().UtcDateTime.Date, TimeSpan.Zero);
        var catalogTime = today.AddDays(-20).AddHours(8);

        var categories = Products.Select(p => p.Category).Distinct()
            .ToDictionary(name => name, name => Category.Create(name, catalogTime));
        db.Categories.AddRange(categories.Values);

        var products = new Dictionary<string, Product>();
        var stock = new Dictionary<Guid, InventoryItem>();
        foreach (var p in Products)
        {
            var product = Product.Create(Sku.Create(p.Sku), p.Name, null, categories[p.Category].Id, new Money(p.Price, "USD"), catalogTime);
            var item = InventoryItem.CreateFor(product.Id, p.Threshold, catalogTime);

            // Opening stock sized so that the seeded orders leave the configured quantity on hand.
            var consumed = Orders.Where(o => o.Status == OrderStatus.Fulfilled)
                .SelectMany(o => o.Lines).Where(l => l.Sku == p.Sku).Sum(l => l.Qty);
            var opening = p.Stock + consumed;
            if (opening > 0)
            {
                item.Adjust(opening, AdjustmentReason.Received, "Opening stock", Actor, catalogTime.AddHours(1));
            }

            if (!p.Active)
            {
                product.Deactivate(catalogTime.AddDays(5));
            }

            products[p.Sku] = product;
            stock[product.Id] = item;
            db.Products.Add(product);
            db.InventoryItems.Add(item);
        }

        await db.SaveChangesAsync(cancellationToken);

        foreach (var o in Orders)
        {
            var created = today.AddDays(-o.DaysAgo).AddHours(o.Hour);
            if (created > clock.GetUtcNow())
            {
                created = clock.GetUtcNow().AddMinutes(-30);
            }

            var lines = o.Lines
                .Select(l => products[l.Sku])
                .Zip(o.Lines, (product, l) => new OrderLineRequest(new OrderProduct(product.Id, product.Sku, product.Name, product.Price), l.Qty))
                .ToList();

            var order = Order.Submit(
                await orderNumbers.NextAsync(cancellationToken), o.Customer, null, lines, Actor, $"seed-{created:yyyyMMddHH}-{o.Customer}",
                "seed", created);
            foreach (var line in order.Lines)
            {
                stock[line.ProductId].Reserve(line.Quantity, order.Id, Actor, created);
            }

            ApplyStatus(order, o.Status, stock, created);
            db.Orders.Add(order);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Seeded {Products} products and {Orders} orders", Products.Length, Orders.Length);
    }

    private static void ApplyStatus(Order order, OrderStatus target, Dictionary<Guid, InventoryItem> stock, DateTimeOffset created)
    {
        if (target == OrderStatus.Submitted)
        {
            return;
        }

        if (target == OrderStatus.Cancelled)
        {
            order.Cancel("Customer changed requirements", Actor, created.AddHours(3));
            foreach (var line in order.Lines)
            {
                stock[line.ProductId].ReleaseReservation(line.Quantity, order.Id, Actor, created.AddHours(3));
            }

            return;
        }

        order.StartProcessing(Actor, created.AddHours(2));
        if (target == OrderStatus.Fulfilled)
        {
            order.Fulfill(Actor, created.AddDays(1));
            foreach (var line in order.Lines)
            {
                stock[line.ProductId].FulfillReservation(line.Quantity, order.Id, Actor, created.AddDays(1));
            }
        }
    }
}

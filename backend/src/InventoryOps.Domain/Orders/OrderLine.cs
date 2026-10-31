using InventoryOps.Domain.Common;

namespace InventoryOps.Domain.Orders;

/// <summary>Immutable order line with product and price snapshots taken at submission time.</summary>
public sealed class OrderLine
{
    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public string Sku { get; private set; } = null!;
    public string ProductName { get; private set; } = null!;
    public decimal UnitPrice { get; private set; }
    public int Quantity { get; private set; }
    public decimal LineTotal { get; private set; }

    private OrderLine()
    {
    }

    internal OrderLine(Guid orderId, OrderProduct product, int quantity)
    {
        if (quantity is <= 0 or > Order.MaxLineQuantity)
        {
            throw new DomainException("orders.quantity_invalid", $"Quantity must be between 1 and {Order.MaxLineQuantity}.");
        }

        Id = Guid.CreateVersion7();
        OrderId = orderId;
        ProductId = product.ProductId;
        Sku = product.Sku;
        ProductName = product.Name;
        UnitPrice = product.UnitPrice.Amount;
        Quantity = quantity;
        LineTotal = product.UnitPrice.Multiply(quantity).Amount;
    }
}

/// <summary>Audit trail entry for an order status transition.</summary>
public sealed class OrderStatusChange
{
    public long Id { get; private set; }
    public Guid OrderId { get; private set; }
    public OrderStatus? FromStatus { get; private set; }
    public OrderStatus ToStatus { get; private set; }
    public string ChangedBy { get; private set; } = null!;
    public string? Reason { get; private set; }
    public DateTimeOffset ChangedAt { get; private set; }

    private OrderStatusChange()
    {
    }

    internal OrderStatusChange(Guid orderId, OrderStatus? from, OrderStatus to, string changedBy, string? reason, DateTimeOffset changedAt)
    {
        OrderId = orderId;
        FromStatus = from;
        ToStatus = to;
        ChangedBy = changedBy;
        Reason = reason;
        ChangedAt = changedAt;
    }
}

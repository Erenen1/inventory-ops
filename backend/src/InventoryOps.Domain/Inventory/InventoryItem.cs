using InventoryOps.Domain.Common;

namespace InventoryOps.Domain.Inventory;

/// <summary>
/// Stock position for a single product. Enforces the inventory invariants:
/// <c>OnHand &gt;= 0</c>, <c>Reserved &gt;= 0</c>, <c>Reserved &lt;= OnHand</c>, and
/// <c>Available = OnHand - Reserved</c>. Every change appends an immutable <see cref="StockMovement"/>.
/// </summary>
/// <remarks>
/// The movement collection is write-only from the aggregate's point of view: it is never loaded,
/// new movements are appended and inserted on save. History is read through dedicated queries.
/// </remarks>
public sealed class InventoryItem : AggregateRoot
{
    public const int MaxQuantity = 1_000_000;

    private readonly List<StockMovement> _movements = [];

    public Guid ProductId { get; private set; }
    public int OnHand { get; private set; }
    public int Reserved { get; private set; }
    public int ReorderThreshold { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public int Available => OnHand - Reserved;
    public bool IsLowStock => Available <= ReorderThreshold;

    /// <summary>Movements appended during the current unit of work.</summary>
    public IReadOnlyCollection<StockMovement> PendingMovements => _movements.AsReadOnly();

    private InventoryItem()
    {
    }

    public static InventoryItem CreateFor(Guid productId, int reorderThreshold, DateTimeOffset now)
    {
        if (productId == Guid.Empty)
        {
            throw new DomainException("inventory.product_required", "Product is required.");
        }

        var item = new InventoryItem { ProductId = productId, UpdatedAt = now };
        item.SetReorderThreshold(reorderThreshold, now);
        return item;
    }

    public void SetReorderThreshold(int threshold, DateTimeOffset now)
    {
        if (threshold is < 0 or > MaxQuantity)
        {
            throw new DomainException("inventory.threshold_invalid", $"Reorder threshold must be between 0 and {MaxQuantity}.");
        }

        var wasLow = IsLowStock;
        ReorderThreshold = threshold;
        UpdatedAt = now;
        RaiseLowStockIfCrossed(wasLow, now);
    }

    /// <summary>Manual stock adjustment. Positive quantities add stock, negative remove it.</summary>
    public void Adjust(int quantityChange, AdjustmentReason reason, string? note, string performedBy, DateTimeOffset now)
    {
        if (quantityChange == 0)
        {
            throw new DomainException("inventory.adjustment_zero", "Adjustment quantity cannot be zero.");
        }

        if (Math.Abs((long)quantityChange) > MaxQuantity)
        {
            throw new DomainException("inventory.adjustment_too_large", $"Adjustment cannot exceed {MaxQuantity} units.");
        }

        if (!Enum.IsDefined(reason))
        {
            throw new DomainException("inventory.reason_invalid", "Adjustment reason is not valid.");
        }

        var newOnHand = (long)OnHand + quantityChange;
        if (newOnHand < 0)
        {
            throw new DomainConflictException("inventory.negative_stock", "Adjustment would make on-hand stock negative.");
        }

        if (newOnHand < Reserved)
        {
            throw new DomainConflictException(
                "inventory.below_reserved",
                $"Adjustment would leave on-hand stock ({newOnHand}) below reserved stock ({Reserved}).");
        }

        if (newOnHand > MaxQuantity)
        {
            throw new DomainException("inventory.too_much_stock", $"On-hand stock cannot exceed {MaxQuantity} units.");
        }

        var wasLow = IsLowStock;
        OnHand = (int)newOnHand;
        UpdatedAt = now;
        Record(StockMovementType.Adjustment, quantityChange, 0, reason, note, null, performedBy, now);
        Raise(new StockAdjusted(ProductId, quantityChange, reason, OnHand, Available, now));
        RaiseLowStockIfCrossed(wasLow, now);
    }

    public void Reserve(int quantity, Guid orderId, string performedBy, DateTimeOffset now)
    {
        EnsurePositive(quantity);
        if (quantity > Available)
        {
            throw new InsufficientStockException(ProductId, quantity, Available);
        }

        var wasLow = IsLowStock;
        Reserved += quantity;
        UpdatedAt = now;
        Record(StockMovementType.Reservation, 0, quantity, null, null, orderId, performedBy, now);
        RaiseLowStockIfCrossed(wasLow, now);
    }

    public void ReleaseReservation(int quantity, Guid orderId, string performedBy, DateTimeOffset now)
    {
        EnsurePositive(quantity);
        if (quantity > Reserved)
        {
            throw new DomainConflictException("inventory.release_exceeds_reserved", "Cannot release more stock than is reserved.");
        }

        Reserved -= quantity;
        UpdatedAt = now;
        Record(StockMovementType.ReservationRelease, 0, -quantity, null, null, orderId, performedBy, now);
    }

    /// <summary>Ships reserved stock: the reservation is consumed and on-hand stock decreases.</summary>
    public void FulfillReservation(int quantity, Guid orderId, string performedBy, DateTimeOffset now)
    {
        EnsurePositive(quantity);
        if (quantity > Reserved)
        {
            throw new DomainConflictException("inventory.fulfill_exceeds_reserved", "Cannot fulfill more stock than is reserved.");
        }

        Reserved -= quantity;
        OnHand -= quantity;
        UpdatedAt = now;
        Record(StockMovementType.Fulfillment, -quantity, -quantity, null, null, orderId, performedBy, now);
    }

    private static void EnsurePositive(int quantity)
    {
        if (quantity is <= 0 or > MaxQuantity)
        {
            throw new DomainException("inventory.quantity_invalid", $"Quantity must be between 1 and {MaxQuantity}.");
        }
    }

    private void RaiseLowStockIfCrossed(bool wasLow, DateTimeOffset now)
    {
        if (!wasLow && IsLowStock)
        {
            Raise(new LowStockDetected(ProductId, Available, ReorderThreshold, now));
        }
    }

    private void Record(
        StockMovementType type, int onHandChange, int reservedChange, AdjustmentReason? reason,
        string? note, Guid? orderId, string performedBy, DateTimeOffset now)
    {
        _movements.Add(new StockMovement(
            ProductId, type, onHandChange, reservedChange, OnHand, Reserved, reason, note, orderId, performedBy, now));
    }
}

public sealed record StockAdjusted(
    Guid ProductId, int QuantityChange, AdjustmentReason Reason, int OnHandAfter, int AvailableAfter, DateTimeOffset OccurredAt)
    : IDomainEvent;

public sealed record LowStockDetected(Guid ProductId, int Available, int ReorderThreshold, DateTimeOffset OccurredAt)
    : IDomainEvent;

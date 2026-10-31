using InventoryOps.Domain.Common;
using InventoryOps.Domain.Inventory;
using Shouldly;

namespace InventoryOps.UnitTests.Domain;

public sealed class InventoryItemTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrderId = Guid.NewGuid();

    private static InventoryItem ItemWithStock(int onHand, int threshold = 2)
    {
        var item = InventoryItem.CreateFor(Guid.NewGuid(), threshold, Now);
        if (onHand > 0)
        {
            item.Adjust(onHand, AdjustmentReason.Received, null, "tester", Now);
        }

        item.ClearDomainEvents();
        return item;
    }

    [Fact]
    public void Adjust_increases_on_hand_and_records_movement()
    {
        var item = ItemWithStock(0);

        item.Adjust(10, AdjustmentReason.Received, "PO-17", "alice", Now);

        item.OnHand.ShouldBe(10);
        item.Available.ShouldBe(10);
        var movement = item.PendingMovements.Last();
        movement.Type.ShouldBe(StockMovementType.Adjustment);
        movement.OnHandChange.ShouldBe(10);
        movement.OnHandAfter.ShouldBe(10);
        movement.Note.ShouldBe("PO-17");
        movement.PerformedBy.ShouldBe("alice");
        item.DomainEvents.OfType<StockAdjusted>().ShouldHaveSingleItem().OnHandAfter.ShouldBe(10);
    }

    [Fact]
    public void Adjust_cannot_make_on_hand_negative()
    {
        var item = ItemWithStock(3);

        var ex = Should.Throw<DomainConflictException>(() => item.Adjust(-4, AdjustmentReason.Damaged, null, "alice", Now));

        ex.Code.ShouldBe("inventory.negative_stock");
        item.OnHand.ShouldBe(3);
    }

    [Fact]
    public void Adjust_cannot_reduce_on_hand_below_reserved()
    {
        var item = ItemWithStock(10);
        item.Reserve(8, OrderId, "alice", Now);

        var ex = Should.Throw<DomainConflictException>(() => item.Adjust(-3, AdjustmentReason.Lost, null, "alice", Now));

        ex.Code.ShouldBe("inventory.below_reserved");
        item.OnHand.ShouldBe(10);
        item.Reserved.ShouldBe(8);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(InventoryItem.MaxQuantity + 1)]
    [InlineData(-(InventoryItem.MaxQuantity + 1))]
    public void Adjust_rejects_zero_and_out_of_range_quantities(int quantity)
    {
        var item = ItemWithStock(5);

        Should.Throw<DomainException>(() => item.Adjust(quantity, AdjustmentReason.CountCorrection, null, "alice", Now));
    }

    [Fact]
    public void Reserve_reduces_available_but_not_on_hand()
    {
        var item = ItemWithStock(10);

        item.Reserve(4, OrderId, "alice", Now);

        item.OnHand.ShouldBe(10);
        item.Reserved.ShouldBe(4);
        item.Available.ShouldBe(6);
        item.PendingMovements.Last().Type.ShouldBe(StockMovementType.Reservation);
    }

    [Fact]
    public void Reserve_more_than_available_throws_insufficient_stock()
    {
        var item = ItemWithStock(5);
        item.Reserve(3, OrderId, "alice", Now);

        var ex = Should.Throw<InsufficientStockException>(() => item.Reserve(3, Guid.NewGuid(), "bob", Now));

        ex.Requested.ShouldBe(3);
        ex.Available.ShouldBe(2);
        item.Reserved.ShouldBe(3);
    }

    [Fact]
    public void Release_returns_reserved_stock_to_available()
    {
        var item = ItemWithStock(10);
        item.Reserve(4, OrderId, "alice", Now);

        item.ReleaseReservation(4, OrderId, "alice", Now);

        item.Reserved.ShouldBe(0);
        item.Available.ShouldBe(10);
    }

    [Fact]
    public void Release_more_than_reserved_is_rejected()
    {
        var item = ItemWithStock(10);
        item.Reserve(2, OrderId, "alice", Now);

        Should.Throw<DomainConflictException>(() => item.ReleaseReservation(3, OrderId, "alice", Now));
    }

    [Fact]
    public void Fulfill_consumes_reservation_and_on_hand()
    {
        var item = ItemWithStock(10);
        item.Reserve(4, OrderId, "alice", Now);

        item.FulfillReservation(4, OrderId, "alice", Now);

        item.OnHand.ShouldBe(6);
        item.Reserved.ShouldBe(0);
        item.Available.ShouldBe(6);
        var movement = item.PendingMovements.Last();
        movement.Type.ShouldBe(StockMovementType.Fulfillment);
        movement.OnHandChange.ShouldBe(-4);
        movement.ReservedChange.ShouldBe(-4);
        movement.OrderId.ShouldBe(OrderId);
    }

    [Fact]
    public void Fulfill_more_than_reserved_is_rejected()
    {
        var item = ItemWithStock(10);
        item.Reserve(1, OrderId, "alice", Now);

        Should.Throw<DomainConflictException>(() => item.FulfillReservation(2, OrderId, "alice", Now));
        item.OnHand.ShouldBe(10);
    }

    [Fact]
    public void Low_stock_event_is_raised_once_when_threshold_is_crossed()
    {
        var item = ItemWithStock(10, threshold: 5);

        item.Reserve(4, OrderId, "alice", Now); // available 6: not low
        item.DomainEvents.OfType<LowStockDetected>().ShouldBeEmpty();

        item.Reserve(2, OrderId, "alice", Now); // available 4: crosses threshold
        item.Reserve(1, OrderId, "alice", Now); // still low: no second event

        item.DomainEvents.OfType<LowStockDetected>().ShouldHaveSingleItem().Available.ShouldBe(4);
        item.IsLowStock.ShouldBeTrue();
    }

    [Fact]
    public void Invariants_hold_across_a_sequence_of_operations()
    {
        var item = ItemWithStock(20);
        item.Reserve(15, OrderId, "a", Now);
        item.FulfillReservation(10, OrderId, "a", Now);
        item.ReleaseReservation(5, OrderId, "a", Now);
        item.Adjust(-10, AdjustmentReason.Damaged, null, "a", Now);

        item.OnHand.ShouldBe(0);
        item.Reserved.ShouldBe(0);
        item.OnHand.ShouldBeGreaterThanOrEqualTo(item.Reserved);
        Should.Throw<InsufficientStockException>(() => item.Reserve(1, OrderId, "a", Now));
    }
}

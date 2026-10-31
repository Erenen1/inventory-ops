using InventoryOps.Domain.Common;
using InventoryOps.Domain.Orders;
using Shouldly;

namespace InventoryOps.UnitTests.Domain;

public sealed class OrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

    private static OrderProduct ProductAt(decimal price, string currency = "USD") =>
        new(Guid.NewGuid(), $"SKU-{Guid.NewGuid():N}"[..12].ToUpperInvariant(), "Widget", new Money(price, currency));

    private static Order Submit(params OrderLineRequest[] lines) =>
        Order.Submit(100_001, "Northwind", null, lines, "alice", "key-1", "hash", Now);

    [Fact]
    public void Submit_snapshots_prices_and_computes_totals_with_decimal_precision()
    {
        var order = Submit(new OrderLineRequest(ProductAt(19.99m), 3), new OrderLineRequest(ProductAt(0.10m), 7));

        order.Status.ShouldBe(OrderStatus.Submitted);
        order.Lines.Select(l => l.LineTotal).ShouldBe([59.97m, 0.70m]);
        order.TotalAmount.ShouldBe(60.67m);
        order.Currency.ShouldBe("USD");
        order.DisplayNumber.ShouldBe("SO-100001");
        order.StatusHistory.ShouldHaveSingleItem().ToStatus.ShouldBe(OrderStatus.Submitted);
        order.DomainEvents.OfType<OrderSubmitted>().ShouldHaveSingleItem().TotalAmount.ShouldBe(60.67m);
    }

    [Fact]
    public void Submit_rejects_duplicate_products()
    {
        var product = ProductAt(5m);

        var ex = Should.Throw<DomainException>(() => Submit(new OrderLineRequest(product, 1), new OrderLineRequest(product, 2)));

        ex.Code.ShouldBe("orders.duplicate_product");
    }

    [Fact]
    public void Submit_rejects_mixed_currencies()
    {
        var ex = Should.Throw<DomainException>(() =>
            Submit(new OrderLineRequest(ProductAt(5m, "USD"), 1), new OrderLineRequest(ProductAt(5m, "EUR"), 1)));

        ex.Code.ShouldBe("orders.mixed_currency");
    }

    [Fact]
    public void Submit_requires_at_least_one_line() =>
        Should.Throw<DomainException>(() => Submit()).Code.ShouldBe("orders.line_count_invalid");

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(Order.MaxLineQuantity + 1)]
    public void Submit_rejects_invalid_quantities(int quantity) =>
        Should.Throw<DomainException>(() => Submit(new OrderLineRequest(ProductAt(1m), quantity))).Code.ShouldBe("orders.quantity_invalid");

    [Fact]
    public void Happy_path_transitions_record_history_and_events()
    {
        var order = Submit(new OrderLineRequest(ProductAt(10m), 1));

        order.StartProcessing("bob", Now.AddHours(1));
        order.Fulfill("bob", Now.AddHours(2));

        order.Status.ShouldBe(OrderStatus.Fulfilled);
        order.StatusHistory.Select(h => h.ToStatus).ShouldBe([OrderStatus.Submitted, OrderStatus.Processing, OrderStatus.Fulfilled]);
        order.StatusHistory[^1].ChangedBy.ShouldBe("bob");
        order.DomainEvents.OfType<OrderFulfilled>().ShouldHaveSingleItem();
        order.UpdatedAt.ShouldBe(Now.AddHours(2));
    }

    [Fact]
    public void Cannot_fulfill_an_order_that_is_not_processing()
    {
        var order = Submit(new OrderLineRequest(ProductAt(10m), 1));

        var ex = Should.Throw<DomainConflictException>(() => order.Fulfill("bob", Now));

        ex.Code.ShouldBe("orders.invalid_transition");
        order.Status.ShouldBe(OrderStatus.Submitted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Submitted_and_processing_orders_can_be_cancelled_with_reason(bool startProcessing)
    {
        var order = Submit(new OrderLineRequest(ProductAt(10m), 1));
        if (startProcessing)
        {
            order.StartProcessing("bob", Now);
        }

        order.Cancel("  customer request ", "bob", Now);

        order.Status.ShouldBe(OrderStatus.Cancelled);
        order.CancellationReason.ShouldBe("customer request");
        order.DomainEvents.OfType<OrderCancelled>().ShouldHaveSingleItem().Reason.ShouldBe("customer request");
    }

    [Fact]
    public void Fulfilled_and_cancelled_orders_are_terminal()
    {
        var fulfilled = Submit(new OrderLineRequest(ProductAt(10m), 1));
        fulfilled.StartProcessing("bob", Now);
        fulfilled.Fulfill("bob", Now);
        var cancelled = Submit(new OrderLineRequest(ProductAt(10m), 1));
        cancelled.Cancel(null, "bob", Now);

        Should.Throw<DomainConflictException>(() => fulfilled.Cancel(null, "bob", Now));
        Should.Throw<DomainConflictException>(() => cancelled.StartProcessing("bob", Now));
        Order.AllowedTransitionsFrom(OrderStatus.Fulfilled).ShouldBeEmpty();
        Order.AllowedTransitionsFrom(OrderStatus.Cancelled).ShouldBeEmpty();
    }

    [Fact]
    public void Allowed_transitions_match_the_documented_state_machine()
    {
        Order.AllowedTransitionsFrom(OrderStatus.Submitted).ShouldBe([OrderStatus.Processing, OrderStatus.Cancelled]);
        Order.AllowedTransitionsFrom(OrderStatus.Processing).ShouldBe([OrderStatus.Fulfilled, OrderStatus.Cancelled]);
    }
}

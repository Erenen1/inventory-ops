using System.Text.Json;
using InventoryOps.Application.Common.Messaging;
using InventoryOps.Application.Modules.Activity;
using InventoryOps.Application.Modules.Catalog;
using InventoryOps.Application.Modules.Catalog.Contracts;
using InventoryOps.Application.Modules.Orders;
using InventoryOps.Domain.Activity;
using InventoryOps.Domain.Inventory;
using InventoryOps.Domain.Orders;
using Shouldly;

namespace InventoryOps.UnitTests.Application;

public sealed class SubmitOrderValidationTests
{
    private readonly SubmitOrderValidator _validator = new();

    [Fact]
    public void Valid_command_passes() =>
        _validator.Validate(new SubmitOrderCommand("Contoso", null, [new SubmitOrderLine(Guid.NewGuid(), 2)])).IsValid.ShouldBeTrue();

    [Fact]
    public void Duplicate_product_lines_are_rejected()
    {
        var id = Guid.NewGuid();
        var result = _validator.Validate(new SubmitOrderCommand("Contoso", null, [new SubmitOrderLine(id, 1), new SubmitOrderLine(id, 1)]));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage.Contains("only once", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(Order.MaxLineQuantity + 1)]
    public void Quantities_outside_bounds_are_rejected(int quantity) =>
        _validator.Validate(new SubmitOrderCommand("Contoso", null, [new SubmitOrderLine(Guid.NewGuid(), quantity)])).IsValid.ShouldBeFalse();

    [Fact]
    public void Empty_customer_and_lines_are_rejected()
    {
        var result = _validator.Validate(new SubmitOrderCommand("", null, []));

        result.Errors.Select(e => e.PropertyName).ShouldContain("CustomerName");
        result.Errors.Select(e => e.PropertyName).ShouldContain("Lines");
    }

    [Fact]
    public void Request_hash_ignores_line_order_but_not_quantities()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        var first = SubmitOrderHandler.ComputeRequestHash(new("Contoso", null, [new(a, 1), new(b, 2)]));
        var reordered = SubmitOrderHandler.ComputeRequestHash(new("Contoso", null, [new(b, 2), new(a, 1)]));
        var changed = SubmitOrderHandler.ComputeRequestHash(new("Contoso", null, [new(a, 1), new(b, 3)]));

        reordered.ShouldBe(first);
        changed.ShouldNotBe(first);
    }
}

public sealed class QueryValidationTests
{
    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public void Paging_limits_are_enforced(int page, int pageSize) =>
        new ProductListQueryValidator().Validate(new ProductListQuery(null, null, null, Page: page, PageSize: pageSize)).IsValid.ShouldBeFalse();

    [Fact]
    public void Sorting_is_restricted_to_an_allowlist() =>
        new ProductListQueryValidator().Validate(new ProductListQuery(null, null, null, SortBy: "PasswordHash")).IsValid.ShouldBeFalse();

    [Fact]
    public void Price_with_more_than_two_decimals_is_rejected() =>
        new CreateProductValidator().Validate(new CreateProductCommand("ABC-1", "Name", null, Guid.NewGuid(), 1.005m, "USD"))
            .IsValid.ShouldBeFalse();
}

public sealed class IntegrationEventTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Domain_events_map_to_versioned_contracts()
    {
        var mapped = IntegrationEventMapper.Map(new OrderSubmitted(Guid.NewGuid(), 100_042, "Contoso", 2, 10.5m, "USD", Now));

        mapped.ShouldNotBeNull();
        mapped.Type.ShouldBe(IntegrationEventTypes.OrderSubmitted);
        mapped.Type.ShouldEndWith(".v1");
        mapped.Payload.ShouldBeOfType<OrderSubmittedV1>().OrderNumber.ShouldBe("SO-100042");
    }

    [Fact]
    public void Low_stock_maps_and_unknown_events_are_ignored()
    {
        IntegrationEventMapper.Map(new LowStockDetected(Guid.NewGuid(), 1, 5, Now))!.Type.ShouldBe(IntegrationEventTypes.LowStockDetected);
        IntegrationEventMapper.Map(new UnpublishedEvent(Now)).ShouldBeNull();
    }

    private sealed record UnpublishedEvent(DateTimeOffset OccurredAt) : InventoryOps.Domain.Common.IDomainEvent;
}

public sealed class ActivityProjectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ActivityProjector _projector = new(new FakeCatalog(), TimeProvider.System);

    private static IntegrationEventEnvelope Envelope(string type, object payload) =>
        new(Guid.NewGuid(), type, Now, "alice@example.com", "corr-1", JsonSerializer.Serialize(payload, Json));

    [Fact]
    public async Task Order_submitted_becomes_a_readable_activity_entry()
    {
        var orderId = Guid.NewGuid();
        var envelope = Envelope(IntegrationEventTypes.OrderSubmitted, new OrderSubmittedV1(orderId, "SO-100001", "Contoso", 2, 1234.5m, "USD"));

        var entry = await _projector.ProjectAsync(envelope, TestContext.Current.CancellationToken);

        entry.ShouldNotBeNull();
        entry.Module.ShouldBe("orders");
        entry.Summary.ShouldBe("Order SO-100001 submitted for Contoso: 2 line(s), 1,234.50 USD.");
        entry.SubjectId.ShouldBe(orderId.ToString());
        entry.SourceMessageId.ShouldBe(envelope.MessageId);
        entry.Actor.ShouldBe("alice@example.com");
    }

    [Fact]
    public async Task Low_stock_is_a_warning_and_resolves_the_sku()
    {
        var envelope = Envelope(IntegrationEventTypes.LowStockDetected, new LowStockDetectedV1(FakeCatalog.KnownProduct, 2, 10));

        var entry = await _projector.ProjectAsync(envelope, TestContext.Current.CancellationToken);

        entry!.Severity.ShouldBe(ActivitySeverity.Warning);
        entry.Summary.ShouldContain("NET-SW-24");
    }

    [Fact]
    public async Task Malformed_payload_is_reported_as_malformed() =>
        await Should.ThrowAsync<MalformedMessageException>(() => _projector.ProjectAsync(
            new IntegrationEventEnvelope(Guid.NewGuid(), IntegrationEventTypes.OrderSubmitted, Now, null, null, "{not json"),
            TestContext.Current.CancellationToken));

    [Fact]
    public async Task Unknown_event_types_are_not_projected() =>
        (await _projector.ProjectAsync(Envelope("billing.invoice-paid.v1", new { }), TestContext.Current.CancellationToken)).ShouldBeNull();

    private sealed class FakeCatalog : ICatalogModule
    {
        public static readonly Guid KnownProduct = Guid.NewGuid();

        public Task<IReadOnlyDictionary<Guid, CatalogProductSnapshot>> GetProductsAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, CatalogProductSnapshot>>(
                productIds.Contains(KnownProduct)
                    ? new Dictionary<Guid, CatalogProductSnapshot> { [KnownProduct] = new(KnownProduct, "NET-SW-24", "Switch", 1m, "USD", true) }
                    : new Dictionary<Guid, CatalogProductSnapshot>());
    }
}

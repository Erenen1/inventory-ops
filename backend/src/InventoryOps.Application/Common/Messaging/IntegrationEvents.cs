using InventoryOps.Domain.Catalog;
using InventoryOps.Domain.Common;
using InventoryOps.Domain.Inventory;
using InventoryOps.Domain.Orders;

namespace InventoryOps.Application.Common.Messaging;

/// <summary>
/// Published event contracts. These are deliberately separate from domain events so the domain
/// can evolve without breaking consumers. The type name embeds a version (<c>.v1</c>); a breaking
/// change introduces a new type (<c>.v2</c>) published alongside the old one until consumers move.
/// </summary>
public static class IntegrationEventTypes
{
    public const string ProductCreated = "catalog.product-created.v1";
    public const string ProductUpdated = "catalog.product-updated.v1";
    public const string ProductDeactivated = "catalog.product-deactivated.v1";
    public const string StockAdjusted = "inventory.stock-adjusted.v1";
    public const string LowStockDetected = "inventory.low-stock-detected.v1";
    public const string OrderSubmitted = "orders.order-submitted.v1";
    public const string OrderProcessingStarted = "orders.order-processing-started.v1";
    public const string OrderFulfilled = "orders.order-fulfilled.v1";
    public const string OrderCancelled = "orders.order-cancelled.v1";
}

public sealed record ProductEventV1(Guid ProductId, string Sku, string Name);

public sealed record StockAdjustedV1(Guid ProductId, int QuantityChange, string Reason, int OnHandAfter, int AvailableAfter);

public sealed record LowStockDetectedV1(Guid ProductId, int Available, int ReorderThreshold);

public sealed record OrderSubmittedV1(Guid OrderId, string OrderNumber, string CustomerName, int LineCount, decimal TotalAmount, string Currency);

public sealed record OrderStatusChangedV1(Guid OrderId, string OrderNumber, string? Reason);

public sealed record OrderFulfilledV1(Guid OrderId, string OrderNumber, decimal TotalAmount, string Currency);

/// <summary>An integration event ready to be stored in the outbox.</summary>
public sealed record IntegrationEvent(string Type, object Payload, DateTimeOffset OccurredAt);

/// <summary>
/// Metadata travelling with every published message. Payload is the serialized contract record.
/// </summary>
public sealed record IntegrationEventEnvelope(
    Guid MessageId, string Type, DateTimeOffset OccurredAt, string? Actor, string? CorrelationId, string Payload);

public static class IntegrationEventMapper
{
    /// <summary>Returns <c>null</c> for domain events that are not published externally.</summary>
    public static IntegrationEvent? Map(IDomainEvent domainEvent) => domainEvent switch
    {
        ProductCreated e => new(IntegrationEventTypes.ProductCreated, new ProductEventV1(e.ProductId, e.Sku, e.Name), e.OccurredAt),
        ProductUpdated e => new(IntegrationEventTypes.ProductUpdated, new ProductEventV1(e.ProductId, e.Sku, e.Name), e.OccurredAt),
        ProductDeactivated e => new(IntegrationEventTypes.ProductDeactivated, new ProductEventV1(e.ProductId, e.Sku, e.Name), e.OccurredAt),
        StockAdjusted e => new(
            IntegrationEventTypes.StockAdjusted,
            new StockAdjustedV1(e.ProductId, e.QuantityChange, e.Reason.ToString(), e.OnHandAfter, e.AvailableAfter),
            e.OccurredAt),
        LowStockDetected e => new(
            IntegrationEventTypes.LowStockDetected, new LowStockDetectedV1(e.ProductId, e.Available, e.ReorderThreshold), e.OccurredAt),
        OrderSubmitted e => new(
            IntegrationEventTypes.OrderSubmitted,
            new OrderSubmittedV1(e.OrderId, Order.FormatNumber(e.OrderNumber), e.CustomerName, e.LineCount, e.TotalAmount, e.Currency),
            e.OccurredAt),
        OrderProcessingStarted e => new(
            IntegrationEventTypes.OrderProcessingStarted, new OrderStatusChangedV1(e.OrderId, Order.FormatNumber(e.OrderNumber), null), e.OccurredAt),
        OrderFulfilled e => new(
            IntegrationEventTypes.OrderFulfilled,
            new OrderFulfilledV1(e.OrderId, Order.FormatNumber(e.OrderNumber), e.TotalAmount, e.Currency),
            e.OccurredAt),
        OrderCancelled e => new(
            IntegrationEventTypes.OrderCancelled, new OrderStatusChangedV1(e.OrderId, Order.FormatNumber(e.OrderNumber), e.Reason), e.OccurredAt),
        _ => null,
    };
}

/// <summary>Raised by consumers when a message cannot be parsed; it must be dead-lettered, not retried.</summary>
public sealed class MalformedMessageException(string message, Exception? inner = null) : Exception(message, inner);

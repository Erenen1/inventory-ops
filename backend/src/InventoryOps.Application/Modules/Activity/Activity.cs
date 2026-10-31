using System.Globalization;
using System.Text.Json;
using InventoryOps.Application.Common;
using InventoryOps.Application.Common.Messaging;
using InventoryOps.Application.Modules.Catalog.Contracts;
using InventoryOps.Domain.Activity;

namespace InventoryOps.Application.Modules.Activity;

public sealed record ActivityEntryDto(
    long Id, string EventType, string Module, ActivitySeverity Severity, string Summary, string? SubjectId,
    string? Actor, DateTimeOffset OccurredAt, DateTimeOffset RecordedAt);

public sealed record ActivityQuery(string? Module, ActivitySeverity? Severity, int Page = 1, int PageSize = Paging.DefaultPageSize);

public interface IActivityReadStore
{
    Task<PagedResult<ActivityEntryDto>> ListAsync(ActivityQuery query, CancellationToken cancellationToken);
}

public sealed class ActivityQueries(IActivityReadStore readStore)
{
    public static readonly IReadOnlySet<string> Modules = new HashSet<string>(StringComparer.Ordinal) { "catalog", "inventory", "orders" };

    public Task<PagedResult<ActivityEntryDto>> ListAsync(ActivityQuery query, CancellationToken cancellationToken)
    {
        var errors = new List<FluentValidation.Results.ValidationFailure>();
        if (query.Page is < 1 or > Paging.MaxPage)
        {
            errors.Add(new("page", "page is out of range."));
        }

        if (query.PageSize is < 1 or > Paging.MaxPageSize)
        {
            errors.Add(new("pageSize", $"pageSize must be between 1 and {Paging.MaxPageSize}."));
        }

        if (query.Module is not null && !Modules.Contains(query.Module))
        {
            errors.Add(new("module", $"module must be one of: {string.Join(", ", Modules)}."));
        }

        return errors.Count > 0 ? throw new FluentValidation.ValidationException(errors) : readStore.ListAsync(query, cancellationToken);
    }
}

/// <summary>
/// Turns integration events into human-readable activity entries. Runs inside the message
/// consumer; idempotency (inbox) and transactions are handled by the caller.
/// </summary>
public sealed class ActivityProjector(ICatalogModule catalog, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <returns>The entry to store, or <c>null</c> when the event type is not projected.</returns>
    /// <exception cref="MalformedMessageException">The payload cannot be read.</exception>
    public async Task<ActivityEntry?> ProjectAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        var projection = envelope.Type switch
        {
            IntegrationEventTypes.ProductCreated => Product(envelope, "created"),
            IntegrationEventTypes.ProductUpdated => Product(envelope, "updated"),
            IntegrationEventTypes.ProductDeactivated => Product(envelope, "deactivated"),
            IntegrationEventTypes.StockAdjusted => await StockAdjustedAsync(envelope, cancellationToken),
            IntegrationEventTypes.LowStockDetected => await LowStockAsync(envelope, cancellationToken),
            IntegrationEventTypes.OrderSubmitted => OrderSubmitted(envelope),
            IntegrationEventTypes.OrderProcessingStarted => OrderStatus(envelope, "moved to processing"),
            IntegrationEventTypes.OrderFulfilled => OrderFulfilled(envelope),
            IntegrationEventTypes.OrderCancelled => OrderStatus(envelope, "cancelled"),
            _ => null,
        };

        return projection is null
            ? null
            : ActivityEntry.Create(
                envelope.MessageId, envelope.Type, projection.Module, projection.Severity, projection.Summary,
                projection.SubjectId, envelope.Actor, envelope.OccurredAt, clock.GetUtcNow());
    }

    private static Projection Product(IntegrationEventEnvelope e, string verb)
    {
        var p = Read<ProductEventV1>(e);
        return new("catalog", ActivitySeverity.Info, $"Product {p.Sku} \"{p.Name}\" {verb}.", p.ProductId.ToString());
    }

    private async Task<Projection> StockAdjustedAsync(IntegrationEventEnvelope e, CancellationToken ct)
    {
        var p = Read<StockAdjustedV1>(e);
        var sku = await SkuAsync(p.ProductId, ct);
        var change = p.QuantityChange.ToString("+#;-#", CultureInfo.InvariantCulture);
        return new("inventory", ActivitySeverity.Info,
            $"Stock for {sku} adjusted by {change} ({p.Reason}). On hand: {p.OnHandAfter}, available: {p.AvailableAfter}.",
            p.ProductId.ToString());
    }

    private async Task<Projection> LowStockAsync(IntegrationEventEnvelope e, CancellationToken ct)
    {
        var p = Read<LowStockDetectedV1>(e);
        var sku = await SkuAsync(p.ProductId, ct);
        return new("inventory", ActivitySeverity.Warning,
            $"Low stock: {sku} has {p.Available} available (reorder threshold {p.ReorderThreshold}).", p.ProductId.ToString());
    }

    private static Projection OrderSubmitted(IntegrationEventEnvelope e)
    {
        var p = Read<OrderSubmittedV1>(e);
        return new("orders", ActivitySeverity.Info,
            $"Order {p.OrderNumber} submitted for {p.CustomerName}: {p.LineCount} line(s), {p.TotalAmount.ToString("N2", CultureInfo.InvariantCulture)} {p.Currency}.",
            p.OrderId.ToString());
    }

    private static Projection OrderFulfilled(IntegrationEventEnvelope e)
    {
        var p = Read<OrderFulfilledV1>(e);
        return new("orders", ActivitySeverity.Info, $"Order {p.OrderNumber} fulfilled.", p.OrderId.ToString());
    }

    private static Projection OrderStatus(IntegrationEventEnvelope e, string verb)
    {
        var p = Read<OrderStatusChangedV1>(e);
        var reason = string.IsNullOrWhiteSpace(p.Reason) ? string.Empty : $" Reason: {p.Reason}";
        return new("orders", ActivitySeverity.Info, $"Order {p.OrderNumber} {verb}.{reason}", p.OrderId.ToString());
    }

    private async Task<string> SkuAsync(Guid productId, CancellationToken ct)
    {
        var products = await catalog.GetProductsAsync([productId], ct);
        return products.TryGetValue(productId, out var product) ? product.Sku : productId.ToString();
    }

    private static T Read<T>(IntegrationEventEnvelope envelope)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(envelope.Payload, Json)
                ?? throw new MalformedMessageException($"Message {envelope.MessageId} has an empty payload.");
        }
        catch (JsonException ex)
        {
            throw new MalformedMessageException($"Message {envelope.MessageId} payload is not a valid {envelope.Type}.", ex);
        }
    }

    private sealed record Projection(string Module, ActivitySeverity Severity, string Summary, string? SubjectId);
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using InventoryOps.Application.Common;
using InventoryOps.Application.Common.Caching;
using InventoryOps.Application.Common.Persistence;
using InventoryOps.Application.Modules.Catalog.Contracts;
using InventoryOps.Application.Modules.Inventory.Contracts;
using InventoryOps.Domain.Common;
using InventoryOps.Domain.Orders;

namespace InventoryOps.Application.Modules.Orders;

public sealed record SubmitOrderLine(Guid ProductId, int Quantity);

public sealed record SubmitOrderCommand(string CustomerName, string? Notes, IReadOnlyList<SubmitOrderLine> Lines);

public sealed record SubmitOrderResult(Guid OrderId, bool Created);

public sealed class SubmitOrderValidator : AbstractValidator<SubmitOrderCommand>
{
    public const int IdempotencyKeyMaxLength = 64;

    public SubmitOrderValidator()
    {
        RuleFor(x => x.CustomerName).NotEmpty().MaximumLength(Order.CustomerNameMaxLength);
        RuleFor(x => x.Notes).MaximumLength(Order.NotesMaxLength);
        RuleFor(x => x.Lines).NotNull().NotEmpty().Must(l => l.Count <= Order.MaxLines)
            .WithMessage($"An order can have at most {Order.MaxLines} lines.");
        RuleFor(x => x.Lines).Must(l => l.Select(x => x.ProductId).Distinct().Count() == l.Count)
            .When(x => x.Lines is not null)
            .WithMessage("Each product may appear only once per order; combine quantities into one line.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEmpty();
            line.RuleFor(l => l.Quantity).InclusiveBetween(1, Order.MaxLineQuantity);
        });
    }
}

/// <summary>
/// Submits an order and reserves its stock in one SQL Server transaction.
/// </summary>
/// <remarks>
/// <para>Idempotency: callers send an <c>Idempotency-Key</c> header (scoped per user). A repeated key
/// with the same payload returns the original order; with a different payload it is rejected (409).
/// The key is stored on the order under a unique index, so it lives as long as the order and two
/// concurrent submissions with the same key cannot both commit.</para>
/// <para>Consistency: product snapshots, stock reservations, the order and its outbox event are written
/// atomically. Concurrent reservations of the same stock are serialised by row-version checks.</para>
/// </remarks>
public sealed class SubmitOrderHandler(
    IOrderRepository orders,
    IOrderNumberGenerator numbers,
    ICatalogModule catalog,
    IStockReservations reservations,
    IUnitOfWork unitOfWork,
    ICacheStore cache,
    ICurrentUser currentUser,
    IValidator<SubmitOrderCommand> validator,
    TimeProvider clock)
{
    public async Task<SubmitOrderResult> HandleAsync(SubmitOrderCommand command, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > SubmitOrderValidator.IdempotencyKeyMaxLength)
        {
            throw new ValidationException(
                [new FluentValidation.Results.ValidationFailure("Idempotency-Key", $"An Idempotency-Key header of 1-{SubmitOrderValidator.IdempotencyKeyMaxLength} characters is required.")]);
        }

        await validator.ValidateAndThrowAsync(command, cancellationToken);
        var actor = currentUser.RequireUserName();
        var requestHash = ComputeRequestHash(command);

        var existing = await FindReplayAsync(actor, idempotencyKey, requestHash, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var orderId = await unitOfWork.ExecuteAsync(ct => SubmitAsync(command, actor, idempotencyKey, requestHash, ct), cancellationToken);
            await cache.RemoveAsync(cancellationToken, CacheKeys.DashboardSummary);
            return new SubmitOrderResult(orderId, Created: true);
        }
        catch (UniqueConstraintViolationException)
        {
            // A concurrent request with the same key committed first.
            var replay = await FindReplayAsync(actor, idempotencyKey, requestHash, cancellationToken);
            if (replay is null)
            {
                throw;
            }

            return replay;
        }
    }

    private async Task<Guid> SubmitAsync(SubmitOrderCommand command, string actor, string idempotencyKey, string requestHash, CancellationToken ct)
    {
        var productIds = command.Lines.Select(l => l.ProductId).ToList();
        var products = await catalog.GetProductsAsync(productIds, ct);

        var lineRequests = new List<OrderLineRequest>(command.Lines.Count);
        foreach (var line in command.Lines)
        {
            if (!products.TryGetValue(line.ProductId, out var product))
            {
                throw new DomainException("orders.product_not_found", $"Product {line.ProductId} does not exist.");
            }

            if (!product.IsActive)
            {
                throw new DomainException("orders.product_inactive", $"Product {product.Sku} is inactive and cannot be ordered.");
            }

            var snapshot = new OrderProduct(product.ProductId, product.Sku, product.Name, new Money(product.UnitPrice, product.Currency));
            lineRequests.Add(new OrderLineRequest(snapshot, line.Quantity));
        }

        var now = clock.GetUtcNow();
        var number = await numbers.NextAsync(ct);
        var order = Order.Submit(number, command.CustomerName, command.Notes, lineRequests, actor, idempotencyKey, requestHash, now);

        await reservations.ReserveAsync(order.Id, order.Lines.Select(l => new StockLine(l.ProductId, l.Quantity)).ToList(), actor, now, ct);
        orders.Add(order);
        return order.Id;
    }

    private async Task<SubmitOrderResult?> FindReplayAsync(string actor, string key, string requestHash, CancellationToken ct)
    {
        var existing = await orders.FindByIdempotencyKeyAsync(actor, key, ct);
        if (existing is null)
        {
            return null;
        }

        if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw new ConflictException(
                "orders.idempotency_key_reused",
                "This Idempotency-Key was already used for a different order request.");
        }

        return new SubmitOrderResult(existing.Id, Created: false);
    }

    /// <summary>Hash of a canonical form of the request (lines sorted by product) for replay detection.</summary>
    internal static string ComputeRequestHash(SubmitOrderCommand command)
    {
        var builder = new StringBuilder();
        builder.Append(command.CustomerName.Trim()).Append('\n').Append(command.Notes?.Trim()).Append('\n');
        foreach (var line in command.Lines.OrderBy(l => l.ProductId))
        {
            builder.Append(line.ProductId.ToString("N")).Append(':').Append(line.Quantity.ToString(CultureInfo.InvariantCulture)).Append(';');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }
}

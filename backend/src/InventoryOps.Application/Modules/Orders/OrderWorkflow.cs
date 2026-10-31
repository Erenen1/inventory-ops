using FluentValidation;
using InventoryOps.Application.Common;
using InventoryOps.Application.Common.Caching;
using InventoryOps.Application.Common.Persistence;
using InventoryOps.Application.Modules.Inventory.Contracts;
using InventoryOps.Domain.Orders;

namespace InventoryOps.Application.Modules.Orders;

public sealed record CancelOrderCommand(string? Reason);

public sealed class CancelOrderValidator : AbstractValidator<CancelOrderCommand>
{
    public CancelOrderValidator() => RuleFor(x => x.Reason).MaximumLength(Order.ReasonMaxLength);
}

/// <summary>
/// Order state transitions after submission. Each transition and its stock effect commit in one
/// transaction; the order's row version prevents two users from applying conflicting transitions.
/// </summary>
public sealed class OrderWorkflow(
    IOrderRepository orders,
    IStockReservations reservations,
    IUnitOfWork unitOfWork,
    ICacheStore cache,
    ICurrentUser currentUser,
    IValidator<CancelOrderCommand> cancelValidator,
    TimeProvider clock)
{
    public Task StartProcessingAsync(Guid orderId, CancellationToken cancellationToken) =>
        RunAsync(orderId, (order, actor, now, _) =>
        {
            order.StartProcessing(actor, now);
            return Task.CompletedTask;
        }, cancellationToken);

    /// <summary>Consumes the reservations: reserved and on-hand stock both decrease.</summary>
    public Task FulfillAsync(Guid orderId, CancellationToken cancellationToken) =>
        RunAsync(orderId, async (order, actor, now, ct) =>
        {
            order.Fulfill(actor, now);
            await reservations.FulfillAsync(order.Id, StockLines(order), actor, now, ct);
        }, cancellationToken);

    /// <summary>Releases the reservations back to available stock; the order is kept for history.</summary>
    public async Task CancelAsync(Guid orderId, CancelOrderCommand command, CancellationToken cancellationToken)
    {
        await cancelValidator.ValidateAndThrowAsync(command, cancellationToken);
        await RunAsync(orderId, async (order, actor, now, ct) =>
        {
            order.Cancel(command.Reason, actor, now);
            await reservations.ReleaseAsync(order.Id, StockLines(order), actor, now, ct);
        }, cancellationToken);
    }

    private async Task RunAsync(
        Guid orderId, Func<Order, string, DateTimeOffset, CancellationToken, Task> transition, CancellationToken cancellationToken)
    {
        var actor = currentUser.RequireUserName();
        await unitOfWork.ExecuteAsync(
            async ct =>
            {
                var order = await orders.GetAsync(orderId, ct) ?? throw new NotFoundException("Order", orderId);
                await transition(order, actor, clock.GetUtcNow(), ct);
            },
            cancellationToken);

        await cache.RemoveAsync(cancellationToken, CacheKeys.DashboardSummary);
    }

    private static List<StockLine> StockLines(Order order) =>
        order.Lines.Select(l => new StockLine(l.ProductId, l.Quantity)).ToList();
}

public sealed class OrderListQueryValidator : AbstractValidator<OrderListQuery>
{
    public OrderListQueryValidator()
    {
        RuleFor(x => x.Search).MaximumLength(Paging.MaxSearchLength);
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.SortBy).Must(OrderSortFields.Allowed.Contains)
            .WithMessage($"sortBy must be one of: {string.Join(", ", OrderSortFields.Allowed)}.");
        RuleFor(x => x.SortDirection).IsInEnum();
        RuleFor(x => x).Must(x => x.From is null || x.To is null || x.From <= x.To).WithMessage("from must be before to.")
            .OverridePropertyName("from");
        this.ApplyPagingRules(x => x.Page, x => x.PageSize);
    }
}

public sealed class OrderQueries(IOrderReadStore readStore, IValidator<OrderListQuery> validator)
{
    public async Task<PagedResult<OrderListItem>> ListAsync(OrderListQuery query, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(query, cancellationToken);
        return await readStore.ListAsync(query, cancellationToken);
    }

    public async Task<OrderDetails> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await readStore.GetAsync(id, cancellationToken) ?? throw new NotFoundException("Order", id);
}

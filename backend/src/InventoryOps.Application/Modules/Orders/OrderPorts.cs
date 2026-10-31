using InventoryOps.Application.Common;
using InventoryOps.Domain.Orders;

namespace InventoryOps.Application.Modules.Orders;

public interface IOrderRepository
{
    /// <summary>Loads the order aggregate including lines and status history.</summary>
    Task<Order?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<Order?> FindByIdempotencyKeyAsync(string createdBy, string idempotencyKey, CancellationToken cancellationToken);

    void Add(Order order);
}

/// <summary>Allocates human-readable order numbers from a database sequence.</summary>
public interface IOrderNumberGenerator
{
    Task<long> NextAsync(CancellationToken cancellationToken);
}

public sealed record OrderListQuery(
    string? Search,
    OrderStatus? Status,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    string SortBy = OrderSortFields.CreatedAt,
    SortDirection SortDirection = SortDirection.Desc,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize);

public static class OrderSortFields
{
    public const string CreatedAt = "createdAt";
    public const string Number = "number";
    public const string Customer = "customer";
    public const string Total = "total";
    public const string Status = "status";

    public static readonly IReadOnlySet<string> Allowed =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { CreatedAt, Number, Customer, Total, Status };
}

public sealed record OrderListItem(
    Guid Id, string Number, string CustomerName, OrderStatus Status, int LineCount, int TotalQuantity, decimal TotalAmount,
    string Currency, string CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record OrderLineDto(Guid Id, Guid ProductId, string Sku, string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal);

public sealed record OrderStatusChangeDto(OrderStatus? FromStatus, OrderStatus ToStatus, string ChangedBy, string? Reason, DateTimeOffset ChangedAt);

public sealed record OrderDetails(
    Guid Id, string Number, string CustomerName, string? Notes, OrderStatus Status, decimal TotalAmount, string Currency,
    string CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string? CancellationReason,
    IReadOnlyList<OrderLineDto> Lines, IReadOnlyList<OrderStatusChangeDto> History, IReadOnlyList<OrderStatus> AllowedTransitions);

public interface IOrderReadStore
{
    Task<PagedResult<OrderListItem>> ListAsync(OrderListQuery query, CancellationToken cancellationToken);

    Task<OrderDetails?> GetAsync(Guid id, CancellationToken cancellationToken);
}

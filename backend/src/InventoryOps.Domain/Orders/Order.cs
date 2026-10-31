using InventoryOps.Domain.Common;

namespace InventoryOps.Domain.Orders;

public enum OrderStatus
{
    Submitted = 1,
    Processing = 2,
    Fulfilled = 3,
    Cancelled = 4,
}

/// <summary>
/// Product data captured at order time. The Orders module never reads catalog entities directly;
/// it receives this snapshot, so later price or name changes cannot alter historical orders.
/// </summary>
public sealed record OrderProduct(Guid ProductId, string Sku, string Name, Money UnitPrice);

public sealed record OrderLineRequest(OrderProduct Product, int Quantity);

/// <summary>
/// An operational order that reserves stock on submission and consumes it on fulfillment.
/// State machine: Submitted → Processing → Fulfilled; Submitted|Processing → Cancelled.
/// Orders carry no payment semantics.
/// </summary>
public sealed class Order : AggregateRoot
{
    public const int MaxLines = 50;
    public const int MaxLineQuantity = 10_000;
    public const int CustomerNameMaxLength = 200;
    public const int NotesMaxLength = 1000;
    public const int ReasonMaxLength = 500;

    private static readonly Dictionary<OrderStatus, OrderStatus[]> AllowedTransitions = new()
    {
        [OrderStatus.Submitted] = [OrderStatus.Processing, OrderStatus.Cancelled],
        [OrderStatus.Processing] = [OrderStatus.Fulfilled, OrderStatus.Cancelled],
        [OrderStatus.Fulfilled] = [],
        [OrderStatus.Cancelled] = [],
    };

    private readonly List<OrderLine> _lines = [];
    private readonly List<OrderStatusChange> _statusHistory = [];

    public Guid Id { get; private set; }
    public long Number { get; private set; }
    public string CustomerName { get; private set; } = null!;
    public string? Notes { get; private set; }
    public OrderStatus Status { get; private set; }
    public string Currency { get; private set; } = null!;
    public decimal TotalAmount { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public string? CancellationReason { get; private set; }

    /// <summary>Client-supplied key used to make order submission idempotent per user.</summary>
    public string IdempotencyKey { get; private set; } = null!;

    /// <summary>Hash of the normalised request, used to detect key reuse with a different payload.</summary>
    public string RequestHash { get; private set; } = null!;

    public IReadOnlyList<OrderLine> Lines => _lines.AsReadOnly();
    public IReadOnlyList<OrderStatusChange> StatusHistory => _statusHistory.AsReadOnly();

    public string DisplayNumber => FormatNumber(Number);

    private Order()
    {
    }

    public static string FormatNumber(long number) => $"SO-{number:D6}";

    public static Order Submit(
        long number,
        string customerName,
        string? notes,
        IReadOnlyCollection<OrderLineRequest> lines,
        string createdBy,
        string idempotencyKey,
        string requestHash,
        DateTimeOffset now)
    {
        var name = customerName?.Trim() ?? string.Empty;
        if (name.Length is 0 or > CustomerNameMaxLength)
        {
            throw new DomainException("orders.customer_invalid", $"Customer name must be 1-{CustomerNameMaxLength} characters.");
        }

        var trimmedNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (trimmedNotes?.Length > NotesMaxLength)
        {
            throw new DomainException("orders.notes_invalid", $"Notes cannot exceed {NotesMaxLength} characters.");
        }

        if (lines.Count is 0 or > MaxLines)
        {
            throw new DomainException("orders.line_count_invalid", $"An order must have between 1 and {MaxLines} lines.");
        }

        if (lines.Select(l => l.Product.ProductId).Distinct().Count() != lines.Count)
        {
            throw new DomainException("orders.duplicate_product", "Each product may appear only once per order.");
        }

        var currencies = lines.Select(l => l.Product.UnitPrice.Currency).Distinct().ToList();
        if (currencies.Count != 1)
        {
            throw new DomainException("orders.mixed_currency", "All products in an order must use the same currency.");
        }

        if (string.IsNullOrWhiteSpace(createdBy) || string.IsNullOrWhiteSpace(idempotencyKey) || string.IsNullOrWhiteSpace(requestHash))
        {
            throw new DomainException("orders.metadata_required", "Order metadata is incomplete.");
        }

        var order = new Order
        {
            Id = Guid.CreateVersion7(now),
            Number = number,
            CustomerName = name,
            Notes = trimmedNotes,
            Status = OrderStatus.Submitted,
            Currency = currencies[0],
            CreatedBy = createdBy,
            CreatedAt = now,
            UpdatedAt = now,
            IdempotencyKey = idempotencyKey,
            RequestHash = requestHash,
        };

        foreach (var line in lines)
        {
            order._lines.Add(new OrderLine(order.Id, line.Product, line.Quantity));
        }

        order.TotalAmount = order._lines.Sum(l => l.LineTotal);
        order._statusHistory.Add(new OrderStatusChange(order.Id, null, OrderStatus.Submitted, createdBy, null, now));
        order.Raise(new OrderSubmitted(order.Id, order.Number, order.CustomerName, order._lines.Count, order.TotalAmount, order.Currency, now));
        return order;
    }

    public bool CanTransitionTo(OrderStatus target) => AllowedTransitions[Status].Contains(target);

    public void StartProcessing(string performedBy, DateTimeOffset now)
    {
        TransitionTo(OrderStatus.Processing, performedBy, null, now);
        Raise(new OrderProcessingStarted(Id, Number, now));
    }

    /// <summary>
    /// Marks the order fulfilled. The caller is responsible for consuming the stock reservations
    /// in the same transaction (see the Orders application layer).
    /// </summary>
    public void Fulfill(string performedBy, DateTimeOffset now)
    {
        TransitionTo(OrderStatus.Fulfilled, performedBy, null, now);
        Raise(new OrderFulfilled(Id, Number, TotalAmount, Currency, now));
    }

    public void Cancel(string? reason, string performedBy, DateTimeOffset now)
    {
        var trimmedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (trimmedReason?.Length > ReasonMaxLength)
        {
            throw new DomainException("orders.reason_invalid", $"Reason cannot exceed {ReasonMaxLength} characters.");
        }

        TransitionTo(OrderStatus.Cancelled, performedBy, trimmedReason, now);
        CancellationReason = trimmedReason;
        Raise(new OrderCancelled(Id, Number, trimmedReason, now));
    }

    private void TransitionTo(OrderStatus target, string performedBy, string? reason, DateTimeOffset now)
    {
        if (!CanTransitionTo(target))
        {
            throw new DomainConflictException(
                "orders.invalid_transition",
                $"Order {DisplayNumber} cannot move from {Status} to {target}.");
        }

        _statusHistory.Add(new OrderStatusChange(Id, Status, target, performedBy, reason, now));
        Status = target;
        UpdatedAt = now;
    }
}

public sealed record OrderSubmitted(
    Guid OrderId, long OrderNumber, string CustomerName, int LineCount, decimal TotalAmount, string Currency, DateTimeOffset OccurredAt)
    : IDomainEvent;

public sealed record OrderProcessingStarted(Guid OrderId, long OrderNumber, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record OrderFulfilled(Guid OrderId, long OrderNumber, decimal TotalAmount, string Currency, DateTimeOffset OccurredAt)
    : IDomainEvent;

public sealed record OrderCancelled(Guid OrderId, long OrderNumber, string? Reason, DateTimeOffset OccurredAt) : IDomainEvent;

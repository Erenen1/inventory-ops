namespace InventoryOps.Infrastructure.Messaging;

/// <summary>
/// Transactional outbox row. Written in the same SQL transaction as the business change that
/// produced it, then published asynchronously by <see cref="OutboxDispatcher"/>.
/// </summary>
public sealed class OutboxMessage
{
    public const int ErrorMaxLength = 2000;

    public long Id { get; set; }
    public Guid MessageId { get; set; }
    public string Type { get; set; } = null!;
    public string Payload { get; set; } = null!;
    public DateTimeOffset OccurredAt { get; set; }
    public string? Actor { get; set; }
    public string? CorrelationId { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>Set when publishing failed permanently; the row is kept for inspection and manual replay.</summary>
    public DateTimeOffset? DeadLetteredAt { get; set; }

    public string? LastError { get; set; }
}

/// <summary>
/// Inbox (processed-message) record. Inserted in the same transaction as a consumer's side effects
/// so a redelivered message is detected and acknowledged without being applied twice.
/// </summary>
public sealed class InboxMessage
{
    public Guid MessageId { get; set; }
    public string Consumer { get; set; } = null!;
    public string Type { get; set; } = null!;
    public DateTimeOffset ProcessedAt { get; set; }
}

/// <summary>Correlation id of the operation in progress (HTTP request or consumed message).</summary>
public sealed class CorrelationContext
{
    public string? CorrelationId { get; set; }
}

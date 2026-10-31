namespace InventoryOps.Domain.Activity;

public enum ActivitySeverity
{
    Info = 1,
    Warning = 2,
}

/// <summary>
/// Read-optimised operational activity record. Entries are projected asynchronously from
/// integration events, so the feed is eventually consistent with the business tables.
/// </summary>
public sealed class ActivityEntry
{
    public const int SummaryMaxLength = 500;

    public long Id { get; private set; }
    public Guid SourceMessageId { get; private set; }
    public string EventType { get; private set; } = null!;
    public string Module { get; private set; } = null!;
    public ActivitySeverity Severity { get; private set; }
    public string Summary { get; private set; } = null!;
    public string? SubjectId { get; private set; }
    public string? Actor { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }

    private ActivityEntry()
    {
    }

    public static ActivityEntry Create(
        Guid sourceMessageId, string eventType, string module, ActivitySeverity severity, string summary,
        string? subjectId, string? actor, DateTimeOffset occurredAt, DateTimeOffset recordedAt)
    {
        return new ActivityEntry
        {
            SourceMessageId = sourceMessageId,
            EventType = eventType,
            Module = module,
            Severity = severity,
            Summary = summary.Length > SummaryMaxLength ? summary[..SummaryMaxLength] : summary,
            SubjectId = subjectId,
            Actor = actor,
            OccurredAt = occurredAt,
            RecordedAt = recordedAt,
        };
    }
}

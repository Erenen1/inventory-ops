using InventoryOps.Domain.Common;

namespace InventoryOps.Domain.Inventory;

public enum StockMovementType
{
    Adjustment = 1,
    Reservation = 2,
    ReservationRelease = 3,
    Fulfillment = 4,
}

public enum AdjustmentReason
{
    Received = 1,
    CountCorrection = 2,
    Damaged = 3,
    Lost = 4,
    Returned = 5,
    Other = 6,
}

/// <summary>
/// Immutable audit record of a single change to a product's stock position, including the
/// resulting on-hand and reserved quantities so history can be read without replaying it.
/// </summary>
public sealed class StockMovement
{
    public const int NoteMaxLength = 500;

    public long Id { get; private set; }
    public Guid ProductId { get; private set; }
    public StockMovementType Type { get; private set; }
    public int OnHandChange { get; private set; }
    public int ReservedChange { get; private set; }
    public int OnHandAfter { get; private set; }
    public int ReservedAfter { get; private set; }
    public AdjustmentReason? Reason { get; private set; }
    public string? Note { get; private set; }
    public Guid? OrderId { get; private set; }
    public string PerformedBy { get; private set; } = null!;
    public DateTimeOffset OccurredAt { get; private set; }

    private StockMovement()
    {
    }

    internal StockMovement(
        Guid productId, StockMovementType type, int onHandChange, int reservedChange, int onHandAfter,
        int reservedAfter, AdjustmentReason? reason, string? note, Guid? orderId, string performedBy, DateTimeOffset occurredAt)
    {
        var trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmedNote?.Length > NoteMaxLength)
        {
            throw new DomainException("inventory.note_too_long", $"Note cannot exceed {NoteMaxLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(performedBy))
        {
            throw new DomainException("inventory.actor_required", "The user performing the change is required.");
        }

        ProductId = productId;
        Type = type;
        OnHandChange = onHandChange;
        ReservedChange = reservedChange;
        OnHandAfter = onHandAfter;
        ReservedAfter = reservedAfter;
        Reason = reason;
        Note = trimmedNote;
        OrderId = orderId;
        PerformedBy = performedBy;
        OccurredAt = occurredAt;
    }
}

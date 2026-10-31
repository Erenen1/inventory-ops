namespace InventoryOps.Domain.Common;

/// <summary>
/// A business rule was violated. The <see cref="Code"/> is a stable, machine-readable
/// identifier that the API surfaces to clients; the message is safe to show to users.
/// </summary>
public class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// The request conflicts with the current state of the system (for example a state-machine
/// transition that is not allowed, or not enough stock). Mapped to HTTP 409.
/// </summary>
public class DomainConflictException(string code, string message) : DomainException(code, message);

public sealed class InsufficientStockException(Guid productId, int requested, int available)
    : DomainConflictException(
        "inventory.insufficient_stock",
        $"Insufficient stock: requested {requested}, available {available}.")
{
    public Guid ProductId { get; } = productId;
    public int Requested { get; } = requested;
    public int Available { get; } = available;
}

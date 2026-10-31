namespace InventoryOps.Application.Common;

/// <summary>The requested resource does not exist (HTTP 404).</summary>
public sealed class NotFoundException(string resource, object key)
    : Exception($"{resource} '{key}' was not found.")
{
    public string Resource { get; } = resource;
}

/// <summary>
/// The request conflicts with current state outside a single aggregate's rules: duplicate unique
/// values, idempotency-key reuse, or exhausted optimistic-concurrency retries (HTTP 409).
/// </summary>
public sealed class ConflictException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>The caller is authenticated but not allowed to perform this operation (HTTP 403).</summary>
public sealed class ForbiddenException(string message) : Exception(message);

/// <summary>
/// Raised by the persistence layer when a database unique constraint rejects a write that passed
/// the application's pre-checks, typically because of a concurrent request.
/// </summary>
public sealed class UniqueConstraintViolationException(string message, Exception inner) : Exception(message, inner);

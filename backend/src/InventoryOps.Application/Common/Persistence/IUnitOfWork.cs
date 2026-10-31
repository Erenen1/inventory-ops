namespace InventoryOps.Application.Common.Persistence;

/// <summary>
/// Runs a business operation as one SQL Server transaction. Domain events raised by the
/// operation are written to the outbox in the same transaction.
/// </summary>
/// <remarks>
/// The operation may be executed more than once: if the commit fails because of an optimistic
/// concurrency conflict or a deadlock, tracked state is discarded and the whole operation is
/// re-run against fresh data (bounded retries). Operations must therefore load the state they
/// need inside the delegate and must not perform non-transactional side effects there.
/// </remarks>
public interface IUnitOfWork
{
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken);
}

public static class UnitOfWorkExtensions
{
    public static Task ExecuteAsync(this IUnitOfWork unitOfWork, Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteAsync(
            async ct =>
            {
                await operation(ct);
                return true;
            },
            cancellationToken);
}

using InventoryOps.Application.Common;
using InventoryOps.Application.Common.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InventoryOps.Infrastructure.Persistence;

/// <summary>
/// Runs an operation in a SQL Server transaction (READ COMMITTED with row versioning) and retries
/// it on optimistic-concurrency conflicts and deadlocks.
/// </summary>
/// <remarks>
/// Retries re-execute the whole operation after discarding tracked state, so every attempt makes
/// its decisions against freshly read data. Retries are bounded with jittered exponential backoff;
/// when they are exhausted the caller receives a 409 and can safely try again.
/// </remarks>
internal sealed class EfUnitOfWork(InventoryOpsDbContext db, ILogger<EfUnitOfWork> logger) : IUnitOfWork
{
    internal const int MaxAttempts = 6;
    private const int SqlDeadlockVictim = 1205;
    private const int SqlUniqueIndexViolation = 2601;
    private const int SqlUniqueConstraintViolation = 2627;

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                var result = await operation(cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch (Exception ex) when (IsTransientConflict(ex) && attempt < MaxAttempts)
            {
                db.ChangeTracker.Clear();
                var delay = Backoff(attempt);
                logger.LogInformation(
                    "Transaction conflict ({Reason}) on attempt {Attempt}/{MaxAttempts}; retrying in {DelayMs} ms",
                    ex is DbUpdateConcurrencyException ? "row version" : "deadlock", attempt, MaxAttempts, (int)delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex) when (IsTransientConflict(ex))
            {
                db.ChangeTracker.Clear();
                logger.LogWarning("Transaction conflict persisted after {MaxAttempts} attempts", MaxAttempts);
                throw new ConflictException(
                    "concurrency_conflict", "The data was changed by another request at the same time. Please retry.");
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                db.ChangeTracker.Clear();
                throw new UniqueConstraintViolationException("A record with the same unique value already exists.", ex);
            }
            catch
            {
                db.ChangeTracker.Clear();
                throw;
            }
        }
    }

    private static bool IsTransientConflict(Exception ex) =>
        ex is DbUpdateConcurrencyException || FindSqlException(ex) is { Number: SqlDeadlockVictim };

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        FindSqlException(ex) is { Number: SqlUniqueIndexViolation or SqlUniqueConstraintViolation };

    private static SqlException? FindSqlException(Exception? ex)
    {
        while (ex is not null)
        {
            if (ex is SqlException sql)
            {
                return sql;
            }

            ex = ex.InnerException;
        }

        return null;
    }

    private static TimeSpan Backoff(int attempt)
    {
        var baseMs = 15 * Math.Pow(2, attempt - 1);
#pragma warning disable CA5394 // Jitter does not need a cryptographic random source.
        var jitter = Random.Shared.NextDouble() * baseMs;
#pragma warning restore CA5394
        return TimeSpan.FromMilliseconds(baseMs + jitter);
    }
}

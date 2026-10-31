using System.Text;
using InventoryOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace InventoryOps.Infrastructure.Messaging;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    public int BatchSize { get; set; } = 50;
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan LockDuration { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>After this many failed publish attempts a message is parked (dead-lettered) in the outbox table.</summary>
    public int MaxAttempts { get; set; } = 10;

    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Upper bound for waiting on a publisher confirm; a hung broker is treated as unavailable.</summary>
    public TimeSpan PublishTimeout { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// Publishes pending outbox rows to RabbitMQ with publisher confirms.
/// </summary>
/// <remarks>
/// <para>Rows are claimed with <c>UPDLOCK, READPAST</c> and a lease (<c>LockedUntil</c>), so several API
/// instances can run the dispatcher concurrently without publishing the same row at the same time.</para>
/// <para>A row is marked processed only after the broker confirms the persistent message. If the process
/// crashes between the confirm and the database update, the message is published again: delivery is
/// at-least-once and consumers deduplicate by message id.</para>
/// <para>Broker outages do not consume attempts: rows stay pending and the dispatcher backs off until the
/// broker is reachable. Message-specific failures (nack, unroutable return) are retried with exponential
/// backoff up to <see cref="OutboxOptions.MaxAttempts"/>; then the row is parked with <c>DeadLetteredAt</c>
/// and logged as an error for investigation. Ordering across messages is not guaranteed.</para>
/// </remarks>
internal sealed class OutboxDispatcher(
    IServiceScopeFactory scopes,
    RabbitMqConnection rabbit,
    Microsoft.Extensions.Options.IOptions<OutboxOptions> options,
    TimeProvider clock,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private readonly OutboxOptions _options = options.Value;
    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Outbox dispatcher started");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var published = await DispatchBatchAsync(stoppingToken);
                if (published == 0)
                {
                    await Task.Delay(_options.PollInterval, clock, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (BrokerUnavailableException ex)
            {
                logger.LogWarning(
                    "RabbitMQ unavailable ({Error}); {Pending} outbox message(s) remain pending", ex.InnerException?.GetType().Name, ex.Pending);
                await Task.Delay(TimeSpan.FromSeconds(5), clock, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox dispatch iteration failed; retrying shortly");
                await Task.Delay(TimeSpan.FromSeconds(5), clock, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }

        logger.LogInformation("Outbox dispatcher stopped");
    }

    /// <returns>Number of rows handled in this batch.</returns>
    internal async Task<int> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryOpsDbContext>();

        var batch = await ClaimAsync(db, cancellationToken);
        if (batch.Count == 0)
        {
            return 0;
        }

        Exception? brokerFailure = null;
        try
        {
            var channel = await GetChannelAsync(cancellationToken);
            foreach (var message in batch.OrderBy(m => m.Id))
            {
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(_options.PublishTimeout);
                    await PublishAsync(channel, message, timeout.Token);
                    message.ProcessedAt = clock.GetUtcNow();
                    message.LastError = null;
                }
                catch (PublishException ex)
                {
                    // Message-specific failure (nack or unroutable): counts towards parking this message.
                    RecordFailure(message, ex, clock.GetUtcNow());
                }
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && ex is not PublishException)
        {
            // Broker or connection failure: not the message's fault, so attempts are not consumed and
            // messages simply stay pending until the broker is reachable again.
            brokerFailure = ex;
            await ResetChannelAsync();
        }

        foreach (var message in batch)
        {
            message.LockedUntil = null;
        }

        await db.SaveChangesAsync(CancellationToken.None);

        if (brokerFailure is not null)
        {
            throw new BrokerUnavailableException(batch.Count(m => m.ProcessedAt is null), brokerFailure);
        }

        return batch.Count;
    }

    private async Task<List<OutboxMessage>> ClaimAsync(InventoryOpsDbContext db, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var lockedUntil = now.Add(_options.LockDuration);

        // Non-composable statement: claims a batch and returns it in one round trip.
        return await db.OutboxMessages
            .FromSql($"""
                WITH batch AS (
                    SELECT TOP ({_options.BatchSize}) *
                    FROM [messaging].[OutboxMessages] WITH (ROWLOCK, UPDLOCK, READPAST)
                    WHERE [ProcessedAt] IS NULL
                      AND [DeadLetteredAt] IS NULL
                      AND [NextAttemptAt] <= {now}
                      AND ([LockedUntil] IS NULL OR [LockedUntil] < {now})
                    ORDER BY [Id])
                UPDATE batch SET [LockedUntil] = {lockedUntil}
                OUTPUT inserted.*;
                """)
            .AsTracking()
            .ToListAsync(cancellationToken);
    }

    private async Task PublishAsync(IChannel channel, OutboxMessage message, CancellationToken cancellationToken)
    {
        var properties = new BasicProperties
        {
            Persistent = true,
            MessageId = message.MessageId.ToString(),
            Type = message.Type,
            ContentType = "application/json",
            ContentEncoding = "utf-8",
            CorrelationId = message.CorrelationId,
            Timestamp = new AmqpTimestamp(message.OccurredAt.ToUnixTimeSeconds()),
            Headers = new Dictionary<string, object?>
            {
                [MessageHeaders.OccurredAt] = message.OccurredAt.ToString("O"),
                [MessageHeaders.Actor] = message.Actor,
            },
        };

        // With confirmation tracking enabled this completes only after the broker confirms; a nack or
        // an unroutable (mandatory) return surfaces as a PublishException.
        await channel.BasicPublishAsync(
            rabbit.Options.Exchange, message.Type, mandatory: true, properties, Encoding.UTF8.GetBytes(message.Payload), cancellationToken);
    }

    private void RecordFailure(OutboxMessage message, Exception ex, DateTimeOffset now)
    {
        message.Attempts++;
        var error = $"{ex.GetType().Name}: {ex.Message}";
        message.LastError = error.Length > OutboxMessage.ErrorMaxLength ? error[..OutboxMessage.ErrorMaxLength] : error;

        if (message.Attempts >= _options.MaxAttempts)
        {
            message.DeadLetteredAt = now;
            logger.LogError(
                "Outbox message {MessageId} ({MessageType}) parked after {Attempts} failed attempts: {Error}",
                message.MessageId, message.Type, message.Attempts, message.LastError);
            return;
        }

        var backoff = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, message.Attempts), _options.MaxBackoff.TotalSeconds));
        message.NextAttemptAt = now.Add(backoff);
        logger.LogWarning(
            "Publishing outbox message {MessageId} failed (attempt {Attempts}); next attempt in {BackoffSeconds}s: {Error}",
            message.MessageId, message.Attempts, backoff.TotalSeconds, message.LastError);
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        await ResetChannelAsync();
        var connection = await rabbit.GetAsync(cancellationToken);
        _channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);
        return _channel;
    }

    private async Task ResetChannelAsync()
    {
        if (_channel is null)
        {
            return;
        }

        try
        {
            await _channel.DisposeAsync();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Ignoring error while disposing a broken channel");
        }

        _channel = null;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await ResetChannelAsync();
    }
}

internal sealed class BrokerUnavailableException(int pending, Exception inner)
    : Exception("RabbitMQ is unavailable.", inner)
{
    public int Pending { get; } = pending;
}

internal static class MessageHeaders
{
    public const string OccurredAt = "x-occurred-at";
    public const string Actor = "x-actor";
}

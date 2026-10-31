using System.Globalization;
using System.Text;
using InventoryOps.Application.Common.Messaging;
using InventoryOps.Application.Modules.Activity;
using InventoryOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace InventoryOps.Infrastructure.Messaging;

/// <summary>
/// Hosted consumer (part of the monolith's deployment) that projects integration events into the
/// activity feed.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Manual acknowledgements: a message is acked only after its effects are committed.</item>
/// <item>Idempotency: the inbox row and the activity entry are saved in one transaction; a redelivered
/// message finds its inbox row and is acknowledged without side effects.</item>
/// <item>Malformed messages are rejected without requeue and routed to the dead-letter queue.</item>
/// <item>Transient failures (e.g. SQL Server unavailable) are requeued; the quorum queue's delivery
/// limit dead-letters a message that keeps failing, so poison messages cannot loop forever.</item>
/// </list>
/// </remarks>
internal sealed class ActivityConsumer(
    IServiceScopeFactory scopes,
    RabbitMqConnection rabbit,
    TimeProvider clock,
    ILogger<ActivityConsumer> logger) : BackgroundService
{
    public const string ConsumerName = "activity-projection";

    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(2);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await StartConsumingAsync(stoppingToken);
                logger.LogInformation("Activity consumer listening on {Queue}", rabbit.Options.ActivityQueue);
                return; // Automatic recovery keeps the consumer alive across broker restarts.
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Activity consumer could not start ({Error}); retrying in {DelaySeconds}s", ex.GetType().Name, delay.TotalSeconds);
                await Task.Delay(delay, clock, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
            }
        }
    }

    private async Task StartConsumingAsync(CancellationToken stoppingToken)
    {
        var connection = await rabbit.GetAsync(stoppingToken);
        _channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await _channel.BasicQosAsync(0, rabbit.Options.PrefetchCount, global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += (_, delivery) => HandleDeliveryAsync(_channel, delivery, stoppingToken);
        await _channel.BasicConsumeAsync(rabbit.Options.ActivityQueue, autoAck: false, consumer, stoppingToken);
    }

    private async Task HandleDeliveryAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        IntegrationEventEnvelope envelope;
        try
        {
            envelope = ReadEnvelope(delivery);
        }
        catch (MalformedMessageException ex)
        {
            logger.LogError("Rejecting malformed message (delivery {DeliveryTag}) to the dead-letter queue: {Error}", delivery.DeliveryTag, ex.Message);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, CancellationToken.None);
            return;
        }

        using var logScope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["MessageId"] = envelope.MessageId,
            ["MessageType"] = envelope.Type,
            ["CorrelationId"] = envelope.CorrelationId,
        });

        try
        {
            var outcome = await ProcessAsync(envelope, stoppingToken);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, CancellationToken.None);
            logger.LogDebug("Message {Outcome}", outcome);
        }
        catch (MalformedMessageException ex)
        {
            logger.LogError("Dead-lettering message with unreadable payload: {Error}", ex.Message);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, CancellationToken.None);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down: return the message to the queue for another consumer.
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Processing failed (redelivered: {Redelivered}); requeueing", delivery.Redelivered);
            await Task.Delay(TimeSpan.FromSeconds(1), clock, CancellationToken.None);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, CancellationToken.None);
        }
    }

    /// <summary>Applies the message exactly once per message id (inbox + side effect in one transaction).</summary>
    internal async Task<string> ProcessAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CorrelationContext>().CorrelationId = envelope.CorrelationId;
        var db = scope.ServiceProvider.GetRequiredService<InventoryOpsDbContext>();
        var projector = scope.ServiceProvider.GetRequiredService<ActivityProjector>();

        var alreadyProcessed = await db.InboxMessages.AnyAsync(
            m => m.MessageId == envelope.MessageId && m.Consumer == ConsumerName, cancellationToken);
        if (alreadyProcessed)
        {
            return "skipped (duplicate)";
        }

        var entry = await projector.ProjectAsync(envelope, cancellationToken);
        db.InboxMessages.Add(new InboxMessage
        {
            MessageId = envelope.MessageId,
            Consumer = ConsumerName,
            Type = envelope.Type,
            ProcessedAt = clock.GetUtcNow(),
        });

        if (entry is not null)
        {
            db.ActivityEntries.Add(entry);
        }

        try
        {
            // One SaveChanges = one transaction: the inbox row and the projection commit together.
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
        {
            return "skipped (concurrent duplicate)";
        }

        return entry is null ? "acknowledged (not projected)" : "projected";
    }

    internal static IntegrationEventEnvelope ReadEnvelope(BasicDeliverEventArgs delivery)
    {
        var props = delivery.BasicProperties;
        if (!Guid.TryParse(props.MessageId, out var messageId))
        {
            throw new MalformedMessageException("Missing or invalid message id.");
        }

        if (string.IsNullOrWhiteSpace(props.Type))
        {
            throw new MalformedMessageException("Missing message type.");
        }

        var occurredAt = ReadHeader(props, MessageHeaders.OccurredAt) is { } raw
            && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : DateTimeOffset.FromUnixTimeSeconds(props.Timestamp.UnixTime);

        string payload;
        try
        {
            payload = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(delivery.Body.Span);
        }
        catch (DecoderFallbackException ex)
        {
            throw new MalformedMessageException("Payload is not valid UTF-8.", ex);
        }

        return new IntegrationEventEnvelope(messageId, props.Type, occurredAt, ReadHeader(props, MessageHeaders.Actor), props.CorrelationId, payload);
    }

    private static string? ReadHeader(IReadOnlyBasicProperties props, string name) =>
        props.Headers?.TryGetValue(name, out var value) == true
            ? value switch
            {
                byte[] bytes => Encoding.UTF8.GetString(bytes),
                string s => s,
                _ => null,
            }
            : null;

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (_channel is not null)
        {
            await _channel.CloseAsync(cancellationToken);
            await _channel.DisposeAsync();
        }
    }
}

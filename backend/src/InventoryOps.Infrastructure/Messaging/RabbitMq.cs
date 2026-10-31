using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace InventoryOps.Infrastructure.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";

    /// <summary>Durable topic exchange receiving every integration event; routing key = event type.</summary>
    public string Exchange { get; set; } = "inventoryops.events";

    /// <summary>Fanout exchange that receives messages rejected or redelivered too often.</summary>
    public string DeadLetterExchange { get; set; } = "inventoryops.dlx";

    public string ActivityQueue { get; set; } = "inventoryops.activity";
    public string ActivityDeadLetterQueue { get; set; } = "inventoryops.activity.dlq";

    /// <summary>Quorum-queue delivery limit: after this many redeliveries a message is dead-lettered.</summary>
    public int DeliveryLimit { get; set; } = 5;

    public ushort PrefetchCount { get; set; } = 16;

    public bool ConsumerEnabled { get; set; } = true;
    public bool DispatcherEnabled { get; set; } = true;
}

/// <summary>
/// Single long-lived AMQP connection shared by the outbox publisher and the consumers. The client's
/// automatic recovery restores the connection, channels and consumers after broker restarts.
/// </summary>
public sealed class RabbitMqConnection(IOptions<RabbitMqOptions> options, ILogger<RabbitMqConnection> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;

    public RabbitMqOptions Options { get; } = options.Value;

    public bool IsOpen => _connection?.IsOpen ?? false;

    public async Task<IConnection> GetAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true })
        {
            return _connection;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { IsOpen: true })
            {
                return _connection;
            }

            if (_connection is not null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }

            var factory = new ConnectionFactory
            {
                HostName = Options.Host,
                Port = Options.Port,
                UserName = Options.UserName,
                Password = Options.Password,
                VirtualHost = Options.VirtualHost,
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
                RequestedHeartbeat = TimeSpan.FromSeconds(30),
                ClientProvidedName = "inventoryops-api",
            };

            var connection = await factory.CreateConnectionAsync(cancellationToken);
            try
            {
                await DeclareTopologyAsync(connection, cancellationToken);
            }
            catch
            {
                await connection.DisposeAsync();
                throw;
            }

            logger.LogInformation("Connected to RabbitMQ at {Host}:{Port}", Options.Host, Options.Port);
            _connection = connection;
            return connection;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Idempotent declaration of exchanges, queues and bindings.</summary>
    private async Task DeclareTopologyAsync(IConnection connection, CancellationToken cancellationToken)
    {
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(Options.Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(Options.DeadLetterExchange, ExchangeType.Fanout, durable: true, autoDelete: false, cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            Options.ActivityDeadLetterQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum" }, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(Options.ActivityDeadLetterQueue, Options.DeadLetterExchange, string.Empty, cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            Options.ActivityQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-queue-type"] = "quorum",
                ["x-dead-letter-exchange"] = Options.DeadLetterExchange,
                ["x-delivery-limit"] = Options.DeliveryLimit,
            },
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(Options.ActivityQueue, Options.Exchange, "#", cancellationToken: cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _gate.Dispose();
    }
}

/// <summary>A broker outage is Degraded: business writes continue and events wait in the outbox.</summary>
internal sealed class RabbitMqHealthCheck(RabbitMqConnection connection) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(connection.IsOpen
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Degraded("RabbitMQ unavailable; events are buffered in the outbox."));
}

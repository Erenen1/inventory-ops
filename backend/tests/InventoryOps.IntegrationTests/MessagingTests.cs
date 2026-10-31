using System.Net;
using System.Text;
using System.Text.Json;
using InventoryOps.Application.Common;
using InventoryOps.Application.Common.Messaging;
using InventoryOps.Application.Modules.Activity;
using InventoryOps.Infrastructure.Messaging;
using InventoryOps.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using Shouldly;

namespace InventoryOps.IntegrationTests;

[CollectionDefinition(nameof(MessagingCollection), DisableParallelization = true)]
public sealed class MessagingCollection;

/// <summary>
/// End-to-end messaging behaviour against a real broker. Runs without parallelism because one test
/// pauses the shared RabbitMQ container.
/// </summary>
[Collection(nameof(MessagingCollection))]
public sealed class MessagingTests(InfrastructureFixture fixture) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ApiClient _admin = new(fixture.Api);

    public async ValueTask InitializeAsync() => await _admin.LoginAsync(InfrastructureFixture.AdminEmail, InfrastructureFixture.Password);

    public ValueTask DisposeAsync()
    {
        _admin.Dispose();
        return ValueTask.CompletedTask;
    }

    private Task<ActivityEntryDto> ActivityForAsync(Guid subjectId, string eventType) =>
        TestData.EventuallyAsync(async () =>
        {
            var page = await _admin.GetJsonAsync<PagedResult<ActivityEntryDto>>("/api/v1/activity?pageSize=100");
            return page.Items.FirstOrDefault(a => a.SubjectId == subjectId.ToString() && a.EventType == eventType);
        });

    [Fact]
    public async Task Business_changes_flow_through_the_outbox_and_broker_into_the_activity_feed()
    {
        var productId = await TestData.CreateProductAsync(_admin, stock: 5);
        var order = await TestData.SubmitOrderOkAsync(_admin, productId, 1);

        var activity = await ActivityForAsync(order.Id, IntegrationEventTypes.OrderSubmitted);

        activity.Summary.ShouldContain(order.Number);
        activity.Actor.ShouldBe(InfrastructureFixture.AdminEmail);
        await using var db = fixture.CreateDbContext();
        var outbox = await db.OutboxMessages.SingleAsync(m => m.Type == IntegrationEventTypes.OrderSubmitted && m.Payload.Contains(order.Id.ToString()));
        outbox.ProcessedAt.ShouldNotBeNull();
        outbox.Attempts.ShouldBe(0);
    }

    [Fact]
    public async Task Duplicate_deliveries_are_applied_once()
    {
        var messageId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new OrderSubmittedV1(orderId, "SO-999999", "Duplicate Co", 1, 1m, "USD"), Json);

        await PublishRawAsync(messageId, IntegrationEventTypes.OrderSubmitted, payload);
        await PublishRawAsync(messageId, IntegrationEventTypes.OrderSubmitted, payload);
        await ActivityForAsync(orderId, IntegrationEventTypes.OrderSubmitted);
        await Task.Delay(1000); // give the second delivery time to be processed

        await using var db = fixture.CreateDbContext();
        (await db.ActivityEntries.CountAsync(a => a.SourceMessageId == messageId)).ShouldBe(1);
        (await db.InboxMessages.CountAsync(m => m.MessageId == messageId)).ShouldBe(1);
    }

    [Fact]
    public async Task Malformed_messages_are_dead_lettered_instead_of_retried_forever()
    {
        var before = await DeadLetterCountAsync();

        await PublishRawAsync(Guid.NewGuid(), IntegrationEventTypes.OrderSubmitted, "{ this is not json");
        await PublishRawAsync(null, IntegrationEventTypes.OrderSubmitted, "{}");

        await TestData.EventuallyAsync(async () => await DeadLetterCountAsync() >= before + 2 ? "ok" : null);
    }

    [Fact]
    public async Task Broker_outage_does_not_affect_writes_and_events_are_delivered_after_recovery()
    {
        await fixture.RabbitMq.PauseAsync(TestContext.Current.CancellationToken);
        Guid productId;
        try
        {
            productId = await TestData.CreateProductAsync(_admin);
            await Task.Delay(TimeSpan.FromSeconds(3));

            await using var db = fixture.CreateDbContext();
            var pending = await db.OutboxMessages.SingleAsync(m => m.Type == IntegrationEventTypes.ProductCreated && m.Payload.Contains(productId.ToString()));
            pending.ProcessedAt.ShouldBeNull();
            pending.DeadLetteredAt.ShouldBeNull();
            pending.Attempts.ShouldBe(0); // outages never consume a message's retry budget
        }
        finally
        {
            await fixture.RabbitMq.UnpauseAsync(TestContext.Current.CancellationToken);
        }

        var activity = await TestData.EventuallyAsync(
            async () => (await _admin.GetJsonAsync<PagedResult<ActivityEntryDto>>("/api/v1/activity?module=catalog&pageSize=100"))
                .Items.FirstOrDefault(a => a.SubjectId == productId.ToString()),
            TimeSpan.FromSeconds(60));
        activity.EventType.ShouldBe(IntegrationEventTypes.ProductCreated);
    }

    [Fact]
    public async Task Readiness_reports_all_dependencies()
    {
        var response = await _admin.GetAsync("/api/health/ready");

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("\"sqlserver\":\"Healthy\"");
        body.ShouldContain("\"redis\":\"Healthy\"");
        body.ShouldContain("\"rabbitmq\":\"Healthy\"");
    }

    private async Task<IConnection> ConnectAsync() =>
        await new ConnectionFactory
        {
            HostName = fixture.RabbitMq.Hostname,
            Port = fixture.RabbitMq.GetMappedPublicPort(5672),
            UserName = "it",
            Password = "it-password",
        }.CreateConnectionAsync();

    private async Task PublishRawAsync(Guid? messageId, string type, string payload)
    {
        var options = new RabbitMqOptions();
        await using var connection = await ConnectAsync();
        await using var channel = await connection.CreateChannelAsync(new CreateChannelOptions(true, true));
        var properties = new BasicProperties
        {
            MessageId = messageId?.ToString(),
            Type = type,
            Persistent = true,
            Headers = new Dictionary<string, object?> { ["x-occurred-at"] = DateTimeOffset.UtcNow.ToString("O") },
        };
        await channel.BasicPublishAsync(options.Exchange, type, mandatory: true, properties, Encoding.UTF8.GetBytes(payload));
    }

    private async Task<uint> DeadLetterCountAsync()
    {
        await using var connection = await ConnectAsync();
        await using var channel = await connection.CreateChannelAsync();
        return await channel.MessageCountAsync(new RabbitMqOptions().ActivityDeadLetterQueue);
    }
}

using System.Text.Json;
using InventoryOps.Application.Common;
using InventoryOps.Application.Common.Messaging;
using InventoryOps.Domain.Activity;
using InventoryOps.Domain.Catalog;
using InventoryOps.Domain.Common;
using InventoryOps.Domain.Inventory;
using InventoryOps.Domain.Orders;
using InventoryOps.Infrastructure.Identity;
using InventoryOps.Infrastructure.Messaging;
using InventoryOps.Infrastructure.Persistence.Configurations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace InventoryOps.Infrastructure.Persistence;

/// <summary>
/// Single database context for the modular monolith. Each module owns its own SQL schema
/// (<c>catalog</c>, <c>inventory</c>, <c>orders</c>, <c>activity</c>, <c>messaging</c>, <c>identity</c>);
/// sharing one context lets cross-module operations commit in one local transaction.
/// </summary>
public sealed class InventoryOpsDbContext(
    DbContextOptions<InventoryOpsDbContext> options,
    ICurrentUser currentUser,
    CorrelationContext correlation)
    : IdentityDbContext<ApplicationUser, IdentityRole, string>(options)
{
    private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<OrderStatusChange> OrderStatusHistory => Set<OrderStatusChange>();
    public DbSet<ActivityEntry> ActivityEntries => Set<ActivityEntry>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(InventoryOpsDbContext).Assembly);
        builder.ConfigureSequences();
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Explicit precision everywhere; no implicit decimal(18,2) truncation warnings.
        configurationBuilder.Properties<decimal>().HavePrecision(14, 2);
        configurationBuilder.Properties<string>().HaveMaxLength(256);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        AddOutboxMessages();
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        AddOutboxMessages();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <summary>
    /// Converts domain events from tracked aggregates into outbox rows. Because this runs inside
    /// SaveChanges, the rows are inserted in the same transaction as the business data.
    /// </summary>
    private void AddOutboxMessages()
    {
        var aggregates = ChangeTracker.Entries<AggregateRoot>()
            .Select(e => e.Entity)
            .Where(a => a.DomainEvents.Count > 0)
            .ToList();

        foreach (var aggregate in aggregates)
        {
            foreach (var domainEvent in aggregate.DomainEvents)
            {
                if (IntegrationEventMapper.Map(domainEvent) is not { } integrationEvent)
                {
                    continue;
                }

                OutboxMessages.Add(new OutboxMessage
                {
                    MessageId = Guid.CreateVersion7(),
                    Type = integrationEvent.Type,
                    Payload = JsonSerializer.Serialize(integrationEvent.Payload, integrationEvent.Payload.GetType(), PayloadJson),
                    OccurredAt = integrationEvent.OccurredAt,
                    Actor = currentUser.UserName ?? CurrentUserExtensions.SystemActor,
                    CorrelationId = correlation.CorrelationId,
                    NextAttemptAt = integrationEvent.OccurredAt,
                });
            }

            aggregate.ClearDomainEvents();
        }
    }
}

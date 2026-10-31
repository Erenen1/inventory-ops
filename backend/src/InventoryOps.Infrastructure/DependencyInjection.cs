using InventoryOps.Application.Common.Caching;
using InventoryOps.Application.Common.Persistence;
using InventoryOps.Application.Modules.Activity;
using InventoryOps.Application.Modules.Catalog;
using InventoryOps.Application.Modules.Dashboard;
using InventoryOps.Application.Modules.Identity;
using InventoryOps.Application.Modules.Inventory;
using InventoryOps.Application.Modules.Orders;
using InventoryOps.Infrastructure.Caching;
using InventoryOps.Infrastructure.Identity;
using InventoryOps.Infrastructure.Messaging;
using InventoryOps.Infrastructure.Persistence;
using InventoryOps.Infrastructure.Persistence.ReadStores;
using InventoryOps.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace InventoryOps.Infrastructure;

public static class DependencyInjection
{
    public const string DatabaseConnectionName = "Database";
    public const string RedisConnectionName = "Redis";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(DatabaseConnectionName)
            ?? throw new InvalidOperationException($"Connection string '{DatabaseConnectionName}' is required.");

        services.AddScoped<CorrelationContext>();
        services.AddDbContext<InventoryOpsDbContext>(options => options.UseSqlServer(
            connectionString,
            sql =>
            {
                sql.CommandTimeout(30);
            }));

        // Persistence
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IInventoryRepository, InventoryRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IOrderNumberGenerator, SqlOrderNumberGenerator>();
        services.AddScoped<IProductReadStore, CatalogReadStore>();
        services.AddScoped<IInventoryReadStore, InventoryReadStore>();
        services.AddScoped<IOrderReadStore, OrderReadStore>();
        services.AddScoped<IDashboardReadStore, DashboardReadStore>();
        services.AddScoped<IActivityReadStore, ActivityReadStore>();
        services.AddScoped<IUserAdministration, UserAdministration>();

        // Initialisation (migrate command only)
        services.Configure<BootstrapOptions>(configuration.GetSection(BootstrapOptions.SectionName));
        services.AddScoped<DatabaseInitializer>();
        services.AddScoped<DemoDataSeeder>();

        // Redis (optional at runtime: the app degrades to uncached reads)
        var redisConfiguration = configuration.GetConnectionString(RedisConnectionName);
        if (string.IsNullOrWhiteSpace(redisConfiguration))
        {
            services.AddSingleton<ICacheStore, NullCacheStore>();
        }
        else
        {
            services.AddSingleton(sp => new RedisConnection(redisConfiguration, sp.GetRequiredService<ILogger<RedisConnection>>()));
            services.AddSingleton<ICacheStore, RedisCacheStore>();
        }

        // RabbitMQ
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));
        services.AddSingleton<RabbitMqConnection>();

        return services;
    }

    /// <summary>Background workers run inside the API process (no separate deployable).</summary>
    public static IServiceCollection AddMessagingWorkers(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(RabbitMqOptions.SectionName).Get<RabbitMqOptions>() ?? new RabbitMqOptions();
        if (options.DispatcherEnabled)
        {
            services.AddSingleton<OutboxDispatcher>();
            services.AddHostedService(sp => sp.GetRequiredService<OutboxDispatcher>());
        }

        if (options.ConsumerEnabled)
        {
            services.AddSingleton<ActivityConsumer>();
            services.AddHostedService(sp => sp.GetRequiredService<ActivityConsumer>());
        }

        return services;
    }

    public static IHealthChecksBuilder AddInfrastructureHealthChecks(this IHealthChecksBuilder builder, IConfiguration configuration)
    {
        builder.AddDbContextCheck<InventoryOpsDbContext>("sqlserver", tags: ["ready"]);
        if (!string.IsNullOrWhiteSpace(configuration.GetConnectionString(RedisConnectionName)))
        {
            builder.AddCheck<RedisHealthCheck>("redis", tags: ["ready"]);
        }

        builder.AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: ["ready"]);
        return builder;
    }
}

/// <summary>Used when no Redis connection string is configured (e.g. some tests and tools).</summary>
internal sealed class NullCacheStore : ICacheStore
{
    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class => Task.FromResult<T?>(null);

    public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken) where T : class => Task.CompletedTask;

    public Task RemoveAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken) => Task.CompletedTask;
}

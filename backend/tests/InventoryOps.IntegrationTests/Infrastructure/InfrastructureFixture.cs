using InventoryOps.Application.Common;
using InventoryOps.Infrastructure.Messaging;
using InventoryOps.Infrastructure.Persistence;
using InventoryOps.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

[assembly: AssemblyFixture(typeof(InventoryOps.IntegrationTests.Infrastructure.InfrastructureFixture))]

namespace InventoryOps.IntegrationTests.Infrastructure;

/// <summary>
/// Starts real SQL Server, Redis and RabbitMQ containers once per test run, applies migrations with
/// the production initializer (including the least-privilege login) and hosts the API against them.
/// Tests create uniquely named data, so they can run in parallel against one database.
/// </summary>
public sealed class InfrastructureFixture : IAsyncLifetime
{
    public const string Password = "Integration-Test-Pa55w0rd";
    public const string AdminEmail = "admin@it.local";
    public const string ManagerEmail = "manager@it.local";
    public const string ViewerEmail = "viewer@it.local";
    private const string DatabaseName = "InventoryOpsTests";
    private const string AppLogin = "inventoryops_app_it";
    private const string AppPassword = "App-Login-Pa55w0rd!";

    public MsSqlContainer Sql { get; } = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU27-ubuntu-22.04").Build();
    public RedisContainer Redis { get; } = new RedisBuilder("redis:7.4.12-alpine").Build();
    public RabbitMqContainer RabbitMq { get; } = new RabbitMqBuilder("rabbitmq:4.1.8-alpine").WithUsername("it").WithPassword("it-password").Build();

    public string AdminConnectionString { get; private set; } = null!;
    public string AppConnectionString { get; private set; } = null!;

    /// <summary>The shared API host used by most tests.</summary>
    public InventoryOpsFactory Api { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(Sql.StartAsync(), Redis.StartAsync(), RabbitMq.StartAsync());

        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(Sql.GetConnectionString()) { InitialCatalog = DatabaseName };
        AdminConnectionString = builder.ConnectionString;
        AppConnectionString = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(AdminConnectionString)
        {
            UserID = AppLogin,
            Password = AppPassword,
        }.ConnectionString;

        await using (var migrator = CreateFactory(new()
        {
            ["ConnectionStrings:Database"] = AdminConnectionString,
            ["Bootstrap:AppDbLogin"] = AppLogin,
            ["Bootstrap:AppDbPassword"] = AppPassword,
            ["Bootstrap:AdminEmail"] = AdminEmail,
            ["Bootstrap:AdminPassword"] = Password,
            ["RabbitMq:DispatcherEnabled"] = "false",
            ["RabbitMq:ConsumerEnabled"] = "false",
        }))
        {
            await using var scope = migrator.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().RunAsync(CancellationToken.None);
        }

        Api = CreateFactory();

        using var admin = new ApiClient(Api);
        await admin.LoginAsync(AdminEmail, Password);
        await admin.CreateUserAsync(ManagerEmail, "Manager");
        await admin.CreateUserAsync(ViewerEmail, "Viewer");
    }

    /// <summary>Creates an API host wired to the containers; <paramref name="overrides"/> replace any setting.</summary>
    public InventoryOpsFactory CreateFactory(Dictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = AppConnectionString,
            ["ConnectionStrings:Redis"] = Redis.GetConnectionString(),
            ["RabbitMq:Host"] = RabbitMq.Hostname,
            ["RabbitMq:Port"] = RabbitMq.GetMappedPublicPort(5672).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["RabbitMq:UserName"] = "it",
            ["RabbitMq:Password"] = "it-password",
            ["Outbox:PollInterval"] = "00:00:00.200",
            ["Security:RequireHttpsCookies"] = "false",
            ["RateLimiting:LoginPermitsPerMinute"] = "10000",
            ["RateLimiting:ApiPermitsPerMinute"] = "100000",
            ["Serilog:MinimumLevel:Default"] = "Warning",
        };

        foreach (var (key, value) in overrides ?? [])
        {
            settings[key] = value;
        }

        return new InventoryOpsFactory(settings);
    }

    /// <summary>Direct database access for asserting persisted state (bypasses the API and cache).</summary>
    public InventoryOpsDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<InventoryOpsDbContext>().UseSqlServer(AdminConnectionString).Options, new NoUser(), new CorrelationContext());

    public async ValueTask DisposeAsync()
    {
        if (Api is not null)
        {
            await Api.DisposeAsync();
        }

        await Task.WhenAll(Sql.DisposeAsync().AsTask(), Redis.DisposeAsync().AsTask(), RabbitMq.DisposeAsync().AsTask());
    }

    private sealed class NoUser : ICurrentUser
    {
        public string? UserId => null;
        public string? UserName => null;
        public bool IsInRole(string role) => false;
    }
}

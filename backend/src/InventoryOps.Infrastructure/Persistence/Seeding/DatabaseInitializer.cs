using InventoryOps.Application.Modules.Identity;
using InventoryOps.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InventoryOps.Infrastructure.Persistence.Seeding;

public sealed class BootstrapOptions
{
    public const string SectionName = "Bootstrap";

    /// <summary>Least-privilege SQL login used by the API at runtime (created/updated by the migrator).</summary>
    public string? AppDbLogin { get; set; }
    public string? AppDbPassword { get; set; }

    public string? AdminEmail { get; set; }
    public string? AdminPassword { get; set; }
    public string AdminDisplayName { get; set; } = "Administrator";

    /// <summary>Demo data and demo users are opt-in and intended for local development only.</summary>
    public bool SeedDemoData { get; set; }
    public string? DemoManagerEmail { get; set; }
    public string? DemoManagerPassword { get; set; }
    public string? DemoViewerEmail { get; set; }
    public string? DemoViewerPassword { get; set; }
}

/// <summary>
/// One-shot database initialisation executed by the <c>migrate</c> command (a separate container in
/// Docker Compose), never by every API instance at startup. Every step is idempotent.
/// </summary>
public sealed class DatabaseInitializer(
    InventoryOpsDbContext db,
    UserManager<ApplicationUser> users,
    RoleManager<IdentityRole> roles,
    DemoDataSeeder demoData,
    IOptions<BootstrapOptions> options,
    TimeProvider clock,
    ILogger<DatabaseInitializer> logger)
{
    private readonly BootstrapOptions _options = options.Value;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await WaitForSqlServerAsync(cancellationToken);

        logger.LogInformation("Applying database migrations");
        await db.Database.MigrateAsync(cancellationToken);
        await EnableReadCommittedSnapshotAsync(cancellationToken);
        await EnsureApplicationLoginAsync(cancellationToken);

        foreach (var role in Roles.All)
        {
            if (!await roles.RoleExistsAsync(role))
            {
                EnsureSucceeded(await roles.CreateAsync(new IdentityRole(role)), $"create role {role}");
            }
        }

        await EnsureUserAsync(_options.AdminEmail, _options.AdminPassword, _options.AdminDisplayName, Roles.Admin);

        if (_options.SeedDemoData)
        {
            await EnsureUserAsync(_options.DemoManagerEmail, _options.DemoManagerPassword, "Demo Manager", Roles.Manager);
            await EnsureUserAsync(_options.DemoViewerEmail, _options.DemoViewerPassword, "Demo Viewer", Roles.Viewer);
            await demoData.SeedAsync(cancellationToken);
        }

        logger.LogInformation("Database initialisation completed");
    }

    private async Task WaitForSqlServerAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                // Connect to master: the application database may not exist yet.
                var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(db.Database.GetConnectionString()) { InitialCatalog = "master" };
                await using var connection = new Microsoft.Data.SqlClient.SqlConnection(builder.ConnectionString);
                await connection.OpenAsync(cancellationToken);
                return;
            }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (attempt < 30)
            {
                logger.LogInformation("SQL Server not ready (attempt {Attempt}): {Error}", attempt, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
    }

    /// <summary>
    /// Row-versioned READ COMMITTED lets dashboards and lists read without blocking on stock writers.
    /// Writers still take update locks, and optimistic concurrency checks always see committed data.
    /// </summary>
    private async Task EnableReadCommittedSnapshotAsync(CancellationToken cancellationToken)
    {
        var enabled = await db.Database
            .SqlQuery<bool>($"SELECT is_read_committed_snapshot_on AS [Value] FROM sys.databases WHERE name = DB_NAME()")
            .SingleAsync(cancellationToken);
        if (!enabled)
        {
            logger.LogInformation("Enabling READ_COMMITTED_SNAPSHOT");
            await db.Database.ExecuteSqlRawAsync("ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE", cancellationToken);
        }
    }

    /// <summary>
    /// Creates (or rotates the password of) the runtime SQL login and grants only data read/write plus
    /// the order-number sequence. The API never connects as <c>sa</c> or as a schema owner.
    /// </summary>
    private async Task EnsureApplicationLoginAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.AppDbLogin) || string.IsNullOrWhiteSpace(_options.AppDbPassword))
        {
            logger.LogWarning("Bootstrap:AppDbLogin is not configured; skipping least-privilege login provisioning");
            return;
        }

        var login = _options.AppDbLogin;
        var password = _options.AppDbPassword;

        // Identifiers cannot be parameterised; values are passed as parameters and quoted with QUOTENAME.
        await db.Database.ExecuteSqlAsync(
            $"""
            DECLARE @login sysname = {login};
            DECLARE @password nvarchar(128) = {password};
            DECLARE @sql nvarchar(max);
            IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = @login)
                SET @sql = N'CREATE LOGIN ' + QUOTENAME(@login) + N' WITH PASSWORD = ' + QUOTENAME(@password, '''') + N', CHECK_POLICY = ON';
            ELSE
                SET @sql = N'ALTER LOGIN ' + QUOTENAME(@login) + N' WITH PASSWORD = ' + QUOTENAME(@password, '''');
            EXEC (@sql);
            IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @login)
            BEGIN
                SET @sql = N'CREATE USER ' + QUOTENAME(@login) + N' FOR LOGIN ' + QUOTENAME(@login);
                EXEC (@sql);
            END
            SET @sql = N'ALTER ROLE db_datareader ADD MEMBER ' + QUOTENAME(@login) + N'; '
                     + N'ALTER ROLE db_datawriter ADD MEMBER ' + QUOTENAME(@login) + N'; '
                     + N'GRANT UPDATE ON OBJECT::[orders].[OrderNumbers] TO ' + QUOTENAME(@login) + N';';
            EXEC (@sql);
            """,
            cancellationToken);

        logger.LogInformation("Least-privilege SQL login {Login} provisioned", login);
    }

    private async Task EnsureUserAsync(string? email, string? password, string displayName, string role)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogInformation("No credentials configured for the {Role} bootstrap user; skipping", role);
            return;
        }

        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = displayName,
                CreatedAt = clock.GetUtcNow(),
                LockoutEnabled = true,
            };
            EnsureSucceeded(await users.CreateAsync(user, password), $"create {role} user");
            logger.LogInformation("Created {Role} user {Email}", role, email);
        }

        if (!await users.IsInRoleAsync(user, role))
        {
            EnsureSucceeded(await users.AddToRoleAsync(user, role), $"assign role {role}");
        }
    }

    private static void EnsureSucceeded(IdentityResult result, string action)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Failed to {action}: {string.Join("; ", result.Errors.Select(e => e.Description))}");
        }
    }
}

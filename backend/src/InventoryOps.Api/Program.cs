using InventoryOps.Api.Endpoints;
using InventoryOps.Api.Hosting;
using InventoryOps.Application;
using InventoryOps.Infrastructure;
using InventoryOps.Infrastructure.Persistence.Seeding;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Formatting.Compact;

// Command modes of the single deployable:
//   (none)       run the API, including the outbox dispatcher and the activity consumer
//   migrate      apply migrations, provision the runtime SQL login, bootstrap users/demo data, exit
//   healthcheck  probe /api/health/ready on localhost (container health checks; no curl in the image)
if (args.Contains("healthcheck"))
{
    return await ContainerHealthProbe.RunAsync();
}

var isMigrate = args.Contains("migrate");
var builder = WebApplication.CreateBuilder(args);

if (isMigrate)
{
    // Schema changes require elevated rights; the runtime API uses a least-privilege login instead.
    var adminConnection = builder.Configuration.GetConnectionString("DatabaseAdmin")
        ?? throw new InvalidOperationException("ConnectionStrings:DatabaseAdmin is required for the migrate command.");
    builder.Configuration["ConnectionStrings:Database"] = adminConnection;
}

builder.Host.UseSerilog((context, services, logger) =>
{
    logger.ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "InventoryOps.Api");
    if (context.Configuration.GetValue("Serilog:UseJson", !context.HostingEnvironment.IsDevelopment()))
    {
        logger.WriteTo.Console(new RenderedCompactJsonFormatter());
    }
    else
    {
        logger.WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}");
    }
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 1024 * 1024;
    options.AddServerHeader = false;
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApiServices(builder.Configuration, builder.Environment);
if (!isMigrate)
{
    builder.Services.AddMessagingWorkers(builder.Configuration);
}

var app = builder.Build();

if (isMigrate)
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().RunAsync(CancellationToken.None);
    return 0;
}

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseSerilogRequestLogging(options =>
{
    options.EnrichDiagnosticContext = (diagnostics, http) =>
        diagnostics.Set("UserId", http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value);
});

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.UseMiddleware<AntiforgeryValidationMiddleware>();

var api = app.MapGroup("/api/v1");
api.MapAuthEndpoints();
api.MapCatalogEndpoints();
api.MapInventoryEndpoints();
api.MapOrderEndpoints();
api.MapDashboardEndpoints();
api.MapUserEndpoints();

app.MapHealthChecks("/api/health/live", new() { Predicate = _ => false, ResponseWriter = HealthResponseWriter.WriteAsync })
    .AllowAnonymous().DisableRateLimiting();
app.MapHealthChecks("/api/health/ready", new() { Predicate = c => c.Tags.Contains("ready"), ResponseWriter = HealthResponseWriter.WriteAsync })
    .AllowAnonymous().DisableRateLimiting();

if (app.Configuration.GetValue("OpenApi:Enabled", app.Environment.IsDevelopment()))
{
    app.MapOpenApi("/api/openapi/{documentName}.json").AllowAnonymous();
    app.MapScalarApiReference("/api/docs", options => options
            .WithTitle("InventoryOps API")
            .WithOpenApiRoutePattern("/api/openapi/{documentName}.json"))
        .AllowAnonymous();
}

await app.RunAsync();
return 0;

/// <summary>Entry point marker for <c>WebApplicationFactory</c> in integration tests.</summary>
public partial class Program;

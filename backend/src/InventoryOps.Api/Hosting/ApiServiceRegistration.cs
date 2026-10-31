using System.Net;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using InventoryOps.Application.Common;
using InventoryOps.Application.Modules.Identity;
using InventoryOps.Infrastructure;
using InventoryOps.Infrastructure.Identity;
using InventoryOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.OpenApi;

namespace InventoryOps.Api.Hosting;

internal static class ApiServiceRegistration
{
    public const string LoginRateLimitPolicy = "login";
    public const string AntiforgeryHeaderName = "X-XSRF-TOKEN";
    public const string AntiforgeryRequestCookieName = "XSRF-TOKEN";

    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();

        services.ConfigureHttpJsonOptions(options =>
        {
            // Unknown properties are rejected: clients cannot smuggle fields such as ids or status (mass assignment).
            options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        });
        services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);

        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            context.ProblemDetails.Instance = $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}";
            context.ProblemDetails.Extensions["correlationId"] = context.HttpContext.TraceIdentifier;
        });
        services.AddExceptionHandler<GlobalExceptionHandler>();

        services.AddIdentityAndCookies(configuration);
        services.AddAuthorizationPolicies();
        services.AddRateLimiting(configuration);
        services.AddForwardedHeaders(configuration);

        services.AddHealthChecks().AddInfrastructureHealthChecks(configuration);

        if (configuration.GetValue("OpenApi:Enabled", environment.IsDevelopment()))
        {
            services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "InventoryOps API",
                    Version = "v1",
                    Description =
                        "Inventory and order management API. Authentication uses an HttpOnly session cookie obtained from " +
                        "POST /api/v1/auth/login. Every state-changing request must send the X-XSRF-TOKEN header with the value of " +
                        "the XSRF-TOKEN cookie (GET /api/v1/auth/csrf issues it). Errors are RFC 9457 problem details with a stable " +
                        "'code' extension: 400 validation, 401 unauthenticated, 403 forbidden, 404 not found, 409 conflict " +
                        "(stock, duplicates, invalid transitions, concurrency), 422 business rule, 429 rate limited.",
                };
                return Task.CompletedTask;
            }));
        }

        return services;
    }

    private static void AddIdentityAndCookies(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.User.RequireUniqueEmail = true;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<InventoryOpsDbContext>()
            .AddSignInManager<AppSignInManager>();

        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();

        // Role or status changes update the security stamp; sessions are re-validated every minute.
        services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(1));

        var securePolicy = configuration.GetValue("Security:RequireHttpsCookies", true)
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = ".InventoryOps.Session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = securePolicy;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;

            // An API never redirects to a login page: return status codes the SPA can act on.
            options.Events.OnRedirectToLogin = context => WriteProblemAsync(context.HttpContext, StatusCodes.Status401Unauthorized, "Authentication required", "unauthenticated");
            options.Events.OnRedirectToAccessDenied = context => WriteProblemAsync(context.HttpContext, StatusCodes.Status403Forbidden, "You do not have permission to perform this action", "forbidden");
        });

        services.AddAntiforgery(options =>
        {
            options.HeaderName = AntiforgeryHeaderName;
            options.Cookie.Name = ".InventoryOps.Antiforgery";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = securePolicy;
        });
    }

    private static void AddAuthorizationPolicies(this IServiceCollection services) =>
        services.AddAuthorizationBuilder()
            // Secure by default: any endpoint without explicit metadata requires a signed-in user.
            .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(Policies.CanRead, p => p.RequireRole(Roles.Admin, Roles.Manager, Roles.Viewer))
            .AddPolicy(Policies.CanOperate, p => p.RequireRole(Roles.Admin, Roles.Manager))
            .AddPolicy(Policies.CanViewActivity, p => p.RequireRole(Roles.Admin, Roles.Manager))
            .AddPolicy(Policies.CanManageUsers, p => p.RequireRole(Roles.Admin));

    private static void AddRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var loginPermits = configuration.GetValue("RateLimiting:LoginPermitsPerMinute", 10);
        var apiPermits = configuration.GetValue("RateLimiting:ApiPermitsPerMinute", 600);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Login: per client IP. Complements Identity's per-account lockout.
            options.AddPolicy(LoginRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                ClientIp(context),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = loginPermits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

            // Everything else: per user when signed in, otherwise per IP.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetTokenBucketLimiter(
                    context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? ClientIp(context),
                    _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = apiPermits,
                        TokensPerPeriod = apiPermits,
                        ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));

            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                await WriteProblemAsync(context.HttpContext, StatusCodes.Status429TooManyRequests, "Too many requests. Try again later.", "rate_limited");
            };
        });
    }

    private static void AddForwardedHeaders(this IServiceCollection services, IConfiguration configuration) =>
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;

            // Only the reverse proxy's network is trusted to set X-Forwarded-* (configured per environment).
            foreach (var network in configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
            {
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            }
        });

    private static string ClientIp(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? IPAddress.None.ToString();

    internal static Task WriteProblemAsync(HttpContext context, int status, string title, string code)
    {
        context.Response.StatusCode = status;
        return Results.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?>
        {
            ["code"] = code,
            ["correlationId"] = context.TraceIdentifier,
        }).ExecuteAsync(context);
    }
}

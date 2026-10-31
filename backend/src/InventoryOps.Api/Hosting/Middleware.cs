using System.Text.RegularExpressions;
using InventoryOps.Infrastructure.Messaging;
using Microsoft.AspNetCore.Antiforgery;
using Serilog.Context;

namespace InventoryOps.Api.Hosting;

/// <summary>
/// Accepts a well-formed <c>X-Correlation-ID</c> from the caller or creates one, echoes it on the
/// response, adds it to every log event and carries it into outbox messages.
/// </summary>
internal sealed partial class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context, CorrelationContext correlation)
    {
        var incoming = context.Request.Headers[HeaderName].ToString();
        var correlationId = SafeId().IsMatch(incoming) ? incoming : Guid.NewGuid().ToString("N");

        correlation.CorrelationId = correlationId;
        context.TraceIdentifier = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }

    [GeneratedRegex("^[A-Za-z0-9._-]{8,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeId();
}

internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Cross-Origin-Resource-Policy"] = "same-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

            // JSON API responses never need to load content; the interactive docs page does.
            if (!context.Request.Path.StartsWithSegments("/api/docs"))
            {
                headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
            }

            if (context.Request.Path.StartsWithSegments("/api"))
            {
                headers.CacheControl = "no-store";
            }

            return Task.CompletedTask;
        });

        return next(context);
    }
}

/// <summary>
/// CSRF protection for cookie-authenticated, state-changing requests (double-submit token bound to
/// the user). Applies to every unsafe method under <c>/api</c>, including login (login CSRF).
/// Runs after authorization so unauthenticated callers receive 401 rather than a CSRF error.
/// </summary>
internal sealed class AntiforgeryValidationMiddleware(RequestDelegate next, IAntiforgery antiforgery, ILogger<AntiforgeryValidationMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (RequiresValidation(context.Request))
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                logger.LogWarning("Rejected request with missing or invalid antiforgery token");
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Invalid antiforgery token",
                        detail: "The request is missing a valid X-XSRF-TOKEN header. Reload the page and try again.",
                        extensions: new Dictionary<string, object?> { ["code"] = "antiforgery_invalid" })
                    .ExecuteAsync(context);
                return;
            }
        }

        await next(context);
    }

    private static bool RequiresValidation(HttpRequest request) =>
        request.Path.StartsWithSegments("/api/v1")
        && !HttpMethods.IsGet(request.Method)
        && !HttpMethods.IsHead(request.Method)
        && !HttpMethods.IsOptions(request.Method);
}

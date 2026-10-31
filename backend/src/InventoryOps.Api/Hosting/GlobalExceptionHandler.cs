using System.Text.Json;
using FluentValidation;
using InventoryOps.Application.Common;
using InventoryOps.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace InventoryOps.Api.Hosting;

/// <summary>
/// Maps exceptions to RFC 9457 ProblemDetails. Business errors carry a stable <c>code</c>; unexpected
/// errors return a generic message plus the correlation id, never exception details.
/// </summary>
internal sealed class GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogInformation("Request was cancelled by the client");
            httpContext.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
            return true;
        }

        var problem = Map(exception);
        if (problem.Status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception");
        }
        else
        {
            logger.LogInformation("Request rejected with {StatusCode}: {ProblemTitle}", problem.Status, problem.Title);
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    private static ProblemDetails Map(Exception exception) => exception switch
    {
        ValidationException ex => new HttpValidationProblemDetails(
            ex.Errors
                .GroupBy(e => JsonNamingPolicy.CamelCase.ConvertName(e.PropertyName))
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray()))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
            Extensions = { ["code"] = "validation_failed" },
        },
        NotFoundException ex => Problem(StatusCodes.Status404NotFound, "Resource not found", ex.Message, "not_found"),
        InsufficientStockException ex => Problem(StatusCodes.Status409Conflict, "Insufficient stock", ex.Message, ex.Code,
            new Dictionary<string, object?> { ["productId"] = ex.ProductId, ["requested"] = ex.Requested, ["available"] = ex.Available }),
        DomainConflictException ex => Problem(StatusCodes.Status409Conflict, "Conflict", ex.Message, ex.Code),
        ConflictException ex => Problem(StatusCodes.Status409Conflict, "Conflict", ex.Message, ex.Code),
        UniqueConstraintViolationException ex => Problem(StatusCodes.Status409Conflict, "Conflict", ex.Message, "duplicate"),
        DomainException ex => Problem(StatusCodes.Status422UnprocessableEntity, "Business rule violated", ex.Message, ex.Code),
        ForbiddenException ex => Problem(StatusCodes.Status403Forbidden, "Forbidden", ex.Message, "forbidden"),
        BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } =>
            Problem(StatusCodes.Status413PayloadTooLarge, "Request too large", "The request body exceeds the allowed size.", "payload_too_large"),
        BadHttpRequestException or JsonException =>
            Problem(StatusCodes.Status400BadRequest, "Malformed request", ReadableBindingError(exception), "malformed_request"),
        _ => Problem(StatusCodes.Status500InternalServerError, "Unexpected error", "An unexpected error occurred. Quote the correlation id when reporting it.", "internal_error"),
    };

    /// <summary>
    /// Binding errors are safe to echo (they describe the request, not the server), but only the
    /// innermost JSON message is used to avoid leaking type names from wrapper exceptions.
    /// </summary>
    private static string ReadableBindingError(Exception exception)
    {
        var json = exception as JsonException ?? exception.InnerException as JsonException;
        if (json is not null)
        {
            return json.Path is null
                ? "The request body is not valid JSON or contains properties that are not allowed."
                : $"The request body is invalid at '{json.Path}': unknown property or wrong value type.";
        }

        return "The request could not be read. Check parameter formats and the request body.";
    }

    private static ProblemDetails Problem(int status, string title, string detail, string code, IDictionary<string, object?>? extensions = null)
    {
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
        problem.Extensions["code"] = code;
        foreach (var (key, value) in extensions ?? new Dictionary<string, object?>())
        {
            problem.Extensions[key] = value;
        }

        return problem;
    }
}

using InventoryOps.Application.Common;
using InventoryOps.Application.Modules.Identity;
using InventoryOps.Application.Modules.Orders;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace InventoryOps.Api.Endpoints;

internal static class OrderEndpoints
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    public static void MapOrderEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/orders").WithTags("Orders");

        group.MapGet("/", async Task<Ok<PagedResult<OrderListItem>>> ([AsParameters] OrderListQuery query, OrderQueries queries, CancellationToken ct) =>
                TypedResults.Ok(await queries.ListAsync(query, ct)))
            .RequireAuthorization(Policies.CanRead);

        group.MapGet("/{id:guid}", async Task<Ok<OrderDetails>> (Guid id, OrderQueries queries, CancellationToken ct) =>
                TypedResults.Ok(await queries.GetAsync(id, ct)))
            .RequireAuthorization(Policies.CanRead)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", async Task<Results<Created<OrderDetails>, Ok<OrderDetails>>> (
                SubmitOrderCommand command,
                [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
                SubmitOrderHandler handler,
                OrderQueries queries,
                CancellationToken ct) =>
            {
                var result = await handler.HandleAsync(command, idempotencyKey ?? string.Empty, ct);
                var order = await queries.GetAsync(result.OrderId, ct);
                return result.Created
                    ? TypedResults.Created($"/api/v1/orders/{order.Id}", order)
                    : TypedResults.Ok(order);
            })
            .RequireAuthorization(Policies.CanOperate)
            .WithSummary("Submits an order and reserves stock atomically. Requires an Idempotency-Key header; replays return 200 with the original order.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/process", async Task<NoContent> (Guid id, OrderWorkflow workflow, CancellationToken ct) =>
            {
                await workflow.StartProcessingAsync(id, ct);
                return TypedResults.NoContent();
            })
            .RequireAuthorization(Policies.CanOperate)
            .WithSummary("Submitted → Processing.")
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/fulfill", async Task<NoContent> (Guid id, OrderWorkflow workflow, CancellationToken ct) =>
            {
                await workflow.FulfillAsync(id, ct);
                return TypedResults.NoContent();
            })
            .RequireAuthorization(Policies.CanOperate)
            .WithSummary("Processing → Fulfilled. Consumes reservations and decreases on-hand stock.")
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/cancel", async Task<NoContent> (Guid id, CancelOrderCommand command, OrderWorkflow workflow, CancellationToken ct) =>
            {
                await workflow.CancelAsync(id, command, ct);
                return TypedResults.NoContent();
            })
            .RequireAuthorization(Policies.CanOperate)
            .WithSummary("Submitted|Processing → Cancelled. Releases reservations.")
            .ProducesProblem(StatusCodes.Status409Conflict);
    }
}

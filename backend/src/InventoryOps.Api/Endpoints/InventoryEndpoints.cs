using InventoryOps.Application.Common;
using InventoryOps.Application.Modules.Identity;
using InventoryOps.Application.Modules.Inventory;
using Microsoft.AspNetCore.Http.HttpResults;

namespace InventoryOps.Api.Endpoints;

internal static class InventoryEndpoints
{
    public static void MapInventoryEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/inventory").WithTags("Inventory");

        group.MapGet("/", async Task<Ok<PagedResult<InventoryItemDto>>> ([AsParameters] InventoryListQuery query, InventoryQueries queries, CancellationToken ct) =>
                TypedResults.Ok(await queries.ListAsync(query, ct)))
            .RequireAuthorization(Policies.CanRead)
            .WithSummary("Stock positions with on-hand, reserved and available quantities. Never cached.");

        group.MapGet("/{productId:guid}", async Task<Ok<InventoryItemDto>> (Guid productId, InventoryQueries queries, CancellationToken ct) =>
                TypedResults.Ok(await queries.GetAsync(productId, ct)))
            .RequireAuthorization(Policies.CanRead);

        group.MapGet("/{productId:guid}/movements", async Task<Ok<PagedResult<StockMovementDto>>> (
                Guid productId, InventoryQueries queries, CancellationToken ct, int page = 1, int pageSize = Paging.DefaultPageSize) =>
                TypedResults.Ok(await queries.ListMovementsAsync(productId, page, pageSize, ct)))
            .RequireAuthorization(Policies.CanRead)
            .WithSummary("Stock movement audit trail, newest first.");

        group.MapPost("/{productId:guid}/adjustments", async Task<NoContent> (
                Guid productId, AdjustStockCommand command, InventoryCommands commands, CancellationToken ct) =>
            {
                await commands.AdjustAsync(productId, command, ct);
                return TypedResults.NoContent();
            })
            .RequireAuthorization(Policies.CanOperate)
            .WithSummary("Manual stock adjustment with a reason. Cannot make on-hand negative or below reserved stock.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{productId:guid}/reorder-threshold", async Task<NoContent> (
                Guid productId, SetReorderThresholdCommand command, InventoryCommands commands, CancellationToken ct) =>
            {
                await commands.SetReorderThresholdAsync(productId, command, ct);
                return TypedResults.NoContent();
            })
            .RequireAuthorization(Policies.CanOperate);
    }
}

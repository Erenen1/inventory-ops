using InventoryOps.Application.Common;
using InventoryOps.Application.Modules.Catalog;
using InventoryOps.Application.Modules.Identity;
using Microsoft.AspNetCore.Http.HttpResults;

namespace InventoryOps.Api.Endpoints;

public sealed record IdResponse(Guid Id);

internal static class CatalogEndpoints
{
    public static void MapCatalogEndpoints(this RouteGroupBuilder api)
    {
        var categories = api.MapGroup("/categories").WithTags("Catalog");
        categories.MapGet("/", async (ProductQueries queries, CancellationToken ct) => TypedResults.Ok(await queries.ListCategoriesAsync(ct)))
            .RequireAuthorization(Policies.CanRead);
        categories.MapPost("/", async Task<Created<IdResponse>> (CreateCategoryCommand command, CreateCategoryHandler handler, CancellationToken ct) =>
            {
                var id = await handler.HandleAsync(command, ct);
                return TypedResults.Created($"/api/v1/categories/{id}", new IdResponse(id));
            })
            .RequireAuthorization(Policies.CanOperate);

        var products = api.MapGroup("/products").WithTags("Catalog");

        products.MapGet("/", async Task<Ok<PagedResult<ProductListItem>>> ([AsParameters] ProductListQuery query, ProductQueries queries, CancellationToken ct) =>
                TypedResults.Ok(await queries.ListAsync(query, ct)))
            .RequireAuthorization(Policies.CanRead)
            .WithSummary("Search, filter, sort and page products (pageSize ≤ 100). Includes live stock figures.");

        products.MapGet("/{id:guid}", async Task<Ok<ProductDetails>> (Guid id, ProductQueries queries, CancellationToken ct) =>
                TypedResults.Ok(await queries.GetAsync(id, ct)))
            .RequireAuthorization(Policies.CanRead)
            .ProducesProblem(StatusCodes.Status404NotFound);

        products.MapPost("/", async Task<Created<IdResponse>> (CreateProductCommand command, CreateProductHandler handler, CancellationToken ct) =>
            {
                var id = await handler.HandleAsync(command, ct);
                return TypedResults.Created($"/api/v1/products/{id}", new IdResponse(id));
            })
            .RequireAuthorization(Policies.CanOperate)
            .WithSummary("Creates a product and starts tracking its stock (on hand = 0).")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        products.MapPut("/{id:guid}", async Task<NoContent> (Guid id, UpdateProductCommand command, UpdateProductHandler handler, CancellationToken ct) =>
            {
                await handler.HandleAsync(id, command, ct);
                return TypedResults.NoContent();
            })
            .RequireAuthorization(Policies.CanOperate)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        products.MapPost("/{id:guid}/deactivate", async Task<NoContent> (Guid id, SetProductActiveHandler handler, CancellationToken ct) =>
            {
                await handler.HandleAsync(id, isActive: false, ct);
                return TypedResults.NoContent();
            })
            .RequireAuthorization(Policies.CanOperate)
            .WithSummary("Removes the product from new orders. History is preserved; products are never deleted.");

        products.MapPost("/{id:guid}/activate", async Task<NoContent> (Guid id, SetProductActiveHandler handler, CancellationToken ct) =>
            {
                await handler.HandleAsync(id, isActive: true, ct);
                return TypedResults.NoContent();
            })
            .RequireAuthorization(Policies.CanOperate);
    }
}

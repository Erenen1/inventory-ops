using System.Security.Claims;
using InventoryOps.Application.Common;
using InventoryOps.Application.Modules.Activity;
using InventoryOps.Application.Modules.Dashboard;
using InventoryOps.Application.Modules.Identity;
using InventoryOps.Domain.Activity;
using Microsoft.AspNetCore.Http.HttpResults;

namespace InventoryOps.Api.Endpoints;

internal static class PlatformEndpoints
{
    public static void MapDashboardEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/dashboard/summary", async Task<Ok<DashboardSummary>> (DashboardQueries queries, CancellationToken ct) =>
                TypedResults.Ok(await queries.GetSummaryAsync(ct)))
            .WithTags("Dashboard")
            .RequireAuthorization(Policies.CanRead)
            .WithSummary("Operational metrics (cached ≤ 30 s, invalidated on writes). Fulfilled value is not revenue.");

        api.MapGet("/activity", async Task<Ok<PagedResult<ActivityEntryDto>>> (
                ActivityQueries queries, CancellationToken ct, string? module = null, ActivitySeverity? severity = null,
                int page = 1, int pageSize = Paging.DefaultPageSize) =>
                TypedResults.Ok(await queries.ListAsync(new ActivityQuery(module, severity, page, pageSize), ct)))
            .WithTags("Activity")
            .RequireAuthorization(Policies.CanViewActivity)
            .WithSummary("Operational activity feed, projected asynchronously from events (eventually consistent).");
    }

    public static void MapUserEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/users").WithTags("Users").RequireAuthorization(Policies.CanManageUsers);

        group.MapGet("/", async Task<Ok<PagedResult<UserDto>>> (
                IUserAdministration users, CancellationToken ct, int page = 1, int pageSize = Paging.DefaultPageSize) =>
            TypedResults.Ok(await users.ListAsync(page, pageSize, ct)));

        group.MapPost("/", async Task<Created<UserDto>> (CreateUserCommand command, IUserAdministration users, CancellationToken ct) =>
            {
                var user = await users.CreateAsync(command, ct);
                return TypedResults.Created($"/api/v1/users/{user.Id}", user);
            })
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{id}/role", async Task<NoContent> (string id, ChangeRoleCommand command, IUserAdministration users, ClaimsPrincipal actor, CancellationToken ct) =>
            {
                await users.ChangeRoleAsync(id, command.Role, ActorId(actor), ct);
                return TypedResults.NoContent();
            })
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{id}/status", async Task<NoContent> (string id, SetUserActiveCommand command, IUserAdministration users, ClaimsPrincipal actor, CancellationToken ct) =>
            {
                await users.SetActiveAsync(id, command.IsActive, ActorId(actor), ct);
                return TypedResults.NoContent();
            })
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static string ActorId(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new ForbiddenException("An authenticated user is required.");
}

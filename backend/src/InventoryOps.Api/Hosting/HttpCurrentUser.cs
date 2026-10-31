using System.Security.Claims;
using InventoryOps.Application.Common;

namespace InventoryOps.Api.Hosting;

internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User is { Identity.IsAuthenticated: true } user ? user : null;

    public string? UserId => Principal?.FindFirstValue(ClaimTypes.NameIdentifier);

    public string? UserName => Principal?.Identity?.Name;

    public bool IsInRole(string role) => Principal?.IsInRole(role) ?? false;
}

namespace InventoryOps.Application.Common;

/// <summary>The identity on whose behalf the current operation runs.</summary>
public interface ICurrentUser
{
    string? UserId { get; }

    /// <summary>Login name (email). Used as the actor recorded in audit trails.</summary>
    string? UserName { get; }

    bool IsInRole(string role);
}

public static class CurrentUserExtensions
{
    public const string SystemActor = "system";

    public static string RequireUserName(this ICurrentUser user) =>
        user.UserName ?? throw new ForbiddenException("An authenticated user is required.");
}

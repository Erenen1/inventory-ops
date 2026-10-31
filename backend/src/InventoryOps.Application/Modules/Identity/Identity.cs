using FluentValidation;
using InventoryOps.Application.Common;

namespace InventoryOps.Application.Modules.Identity;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string Viewer = "Viewer";

    public static readonly IReadOnlyList<string> All = [Admin, Manager, Viewer];
}

/// <summary>Authorization policy names. Every protected endpoint maps to exactly one policy.</summary>
public static class Policies
{
    /// <summary>Read business data (any role).</summary>
    public const string CanRead = "CanRead";

    /// <summary>Create/modify products, stock and orders (Admin, Manager).</summary>
    public const string CanOperate = "CanOperate";

    /// <summary>Read the operational activity log (Admin, Manager).</summary>
    public const string CanViewActivity = "CanViewActivity";

    /// <summary>Manage users and roles (Admin).</summary>
    public const string CanManageUsers = "CanManageUsers";
}

public sealed record UserDto(string Id, string Email, string DisplayName, string Role, bool IsActive, DateTimeOffset? LockoutEnd, DateTimeOffset CreatedAt);

public sealed record CreateUserCommand(string Email, string DisplayName, string Role, string Password);

public sealed record ChangeRoleCommand(string Role);

public sealed record SetUserActiveCommand(bool IsActive);

public sealed class CreateUserValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Role).Must(Roles.All.Contains).WithMessage($"role must be one of: {string.Join(", ", Roles.All)}.");
        RuleFor(x => x.Password).NotEmpty().MinimumLength(12).MaximumLength(128);
    }
}

public sealed class ChangeRoleValidator : AbstractValidator<ChangeRoleCommand>
{
    public ChangeRoleValidator() =>
        RuleFor(x => x.Role).Must(Roles.All.Contains).WithMessage($"role must be one of: {string.Join(", ", Roles.All)}.");
}

/// <summary>
/// User administration port implemented with ASP.NET Core Identity. Implementations must enforce:
/// an administrator cannot demote or deactivate themselves, and the last active administrator
/// cannot be removed. Role and status changes invalidate existing sessions (security stamp).
/// </summary>
public interface IUserAdministration
{
    Task<PagedResult<UserDto>> ListAsync(int page, int pageSize, CancellationToken cancellationToken);

    Task<UserDto> CreateAsync(CreateUserCommand command, CancellationToken cancellationToken);

    Task ChangeRoleAsync(string userId, string role, string actingUserId, CancellationToken cancellationToken);

    Task SetActiveAsync(string userId, bool isActive, string actingUserId, CancellationToken cancellationToken);
}

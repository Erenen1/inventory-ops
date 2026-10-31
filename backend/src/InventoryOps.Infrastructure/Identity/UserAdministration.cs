using FluentValidation;
using FluentValidation.Results;
using InventoryOps.Application.Common;
using InventoryOps.Application.Modules.Identity;
using InventoryOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace InventoryOps.Infrastructure.Identity;

internal sealed class UserAdministration(
    InventoryOpsDbContext db,
    UserManager<ApplicationUser> users,
    IValidator<CreateUserCommand> createValidator,
    TimeProvider clock) : IUserAdministration
{
    public async Task<PagedResult<UserDto>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        if (page is < 1 or > Paging.MaxPage || pageSize is < 1 or > Paging.MaxPageSize)
        {
            throw new ValidationException([new ValidationFailure("pageSize", $"pageSize must be between 1 and {Paging.MaxPageSize}.")]);
        }

        var total = await db.Users.CountAsync(cancellationToken);

        // Single query (users + their role) instead of one role lookup per user.
        var items = await db.Users.AsNoTracking()
            .OrderBy(u => u.Email)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new UserDto(
                u.Id,
                u.Email ?? string.Empty,
                u.DisplayName,
                db.UserRoles.Where(ur => ur.UserId == u.Id)
                    .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
                    .OrderBy(n => n)
                    .FirstOrDefault() ?? Roles.Viewer,
                u.IsActive,
                u.LockoutEnd,
                u.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<UserDto>(items, page, pageSize, total);
    }

    public async Task<UserDto> CreateAsync(CreateUserCommand command, CancellationToken cancellationToken)
    {
        await createValidator.ValidateAndThrowAsync(command, cancellationToken);
        if (await users.FindByEmailAsync(command.Email) is not null)
        {
            throw new ConflictException("identity.duplicate_email", "A user with this email already exists.");
        }

        var user = new ApplicationUser
        {
            UserName = command.Email,
            Email = command.Email,
            EmailConfirmed = true,
            DisplayName = command.DisplayName.Trim(),
            CreatedAt = clock.GetUtcNow(),
            LockoutEnabled = true,
        };

        ThrowIfFailed(await users.CreateAsync(user, command.Password));
        ThrowIfFailed(await users.AddToRoleAsync(user, command.Role));
        return await ToDtoAsync(user);
    }

    public async Task ChangeRoleAsync(string userId, string role, string actingUserId, CancellationToken cancellationToken)
    {
        if (!Roles.All.Contains(role))
        {
            throw new ValidationException([new ValidationFailure("role", $"role must be one of: {string.Join(", ", Roles.All)}.")]);
        }

        var user = await users.FindByIdAsync(userId) ?? throw new NotFoundException("User", userId);
        var currentRoles = await users.GetRolesAsync(user);
        if (currentRoles.Count == 1 && currentRoles[0] == role)
        {
            return;
        }

        if (currentRoles.Contains(Roles.Admin))
        {
            await EnsureAnotherAdminRemainsAsync(user, actingUserId, "change your own role");
        }

        ThrowIfFailed(await users.RemoveFromRolesAsync(user, currentRoles));
        ThrowIfFailed(await users.AddToRoleAsync(user, role));

        // Invalidates existing cookies on the next security-stamp validation.
        await users.UpdateSecurityStampAsync(user);
    }

    public async Task SetActiveAsync(string userId, bool isActive, string actingUserId, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId) ?? throw new NotFoundException("User", userId);
        if (user.IsActive == isActive)
        {
            return;
        }

        if (!isActive && await users.IsInRoleAsync(user, Roles.Admin))
        {
            await EnsureAnotherAdminRemainsAsync(user, actingUserId, "deactivate your own account");
        }

        user.IsActive = isActive;
        ThrowIfFailed(await users.UpdateAsync(user));
        await users.UpdateSecurityStampAsync(user);
    }

    private async Task EnsureAnotherAdminRemainsAsync(ApplicationUser user, string actingUserId, string selfAction)
    {
        if (user.Id == actingUserId)
        {
            throw new ConflictException("identity.self_change", $"You cannot {selfAction}.");
        }

        var activeAdmins = (await users.GetUsersInRoleAsync(Roles.Admin)).Count(u => u.IsActive);
        if (activeAdmins <= 1)
        {
            throw new ConflictException("identity.last_admin", "At least one active administrator must remain.");
        }
    }

    private async Task<UserDto> ToDtoAsync(ApplicationUser user)
    {
        var roles = await users.GetRolesAsync(user);
        return new UserDto(
            user.Id, user.Email ?? string.Empty, user.DisplayName, roles.FirstOrDefault() ?? Roles.Viewer, user.IsActive, user.LockoutEnd,
            user.CreatedAt);
    }

    private static void ThrowIfFailed(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new ValidationException(result.Errors.Select(e => new ValidationFailure(e.Code, e.Description)));
        }
    }
}

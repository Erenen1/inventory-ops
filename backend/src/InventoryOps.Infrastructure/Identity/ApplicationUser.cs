using Microsoft.AspNetCore.Identity;

namespace InventoryOps.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Deactivated users cannot sign in and their existing sessions are invalidated.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
}

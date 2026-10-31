using InventoryOps.Application.Common;
using InventoryOps.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace InventoryOps.Infrastructure.Persistence;

/// <summary>Used only by <c>dotnet ef</c> to build the model when creating migrations.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<InventoryOpsDbContext>
{
    public InventoryOpsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<InventoryOpsDbContext>()
            .UseSqlServer("Server=localhost;Database=InventoryOps;Integrated Security=false")
            .Options;
        return new InventoryOpsDbContext(options, new DesignTimeUser(), new CorrelationContext());
    }

    private sealed class DesignTimeUser : ICurrentUser
    {
        public string? UserId => null;
        public string? UserName => null;
        public bool IsInRole(string role) => false;
    }
}

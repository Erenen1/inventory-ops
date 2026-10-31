using Microsoft.EntityFrameworkCore;

namespace InventoryOps.Infrastructure.Persistence.Configurations;

internal static class SequenceConfiguration
{
    public static void ConfigureSequences(this ModelBuilder builder) =>
        builder.HasSequence<long>(OrderConfiguration.NumberSequence, Schemas.Orders).StartsAt(100_001).IncrementsBy(1);
}

using InventoryOps.Domain.Activity;
using InventoryOps.Infrastructure.Identity;
using InventoryOps.Infrastructure.Messaging;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InventoryOps.Infrastructure.Persistence.Configurations;

internal sealed class ActivityEntryConfiguration : IEntityTypeConfiguration<ActivityEntry>
{
    public void Configure(EntityTypeBuilder<ActivityEntry> builder)
    {
        builder.ToTable("ActivityEntries", Schemas.Activity);
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).UseIdentityColumn();
        builder.Property(a => a.EventType).HasMaxLength(100).IsUnicode(false);
        builder.Property(a => a.Module).HasMaxLength(32).IsUnicode(false);
        builder.Property(a => a.Severity).HasConversion<string>().HasMaxLength(16).IsUnicode(false);
        builder.Property(a => a.Summary).HasMaxLength(ActivityEntry.SummaryMaxLength);
        builder.Property(a => a.SubjectId).HasMaxLength(64);
        builder.Property(a => a.Actor).HasMaxLength(256);

        // Second line of defence against duplicate projection, in addition to the inbox table.
        builder.HasIndex(a => a.SourceMessageId).IsUnique();
        builder.HasIndex(a => a.OccurredAt).IsDescending();
        builder.HasIndex(a => new { a.Module, a.OccurredAt }).IsDescending(false, true);
    }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages", Schemas.Messaging);
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).UseIdentityColumn();
        builder.Property(m => m.Type).HasMaxLength(100).IsUnicode(false);
        builder.Property(m => m.Payload).HasColumnType("nvarchar(max)");
        builder.Property(m => m.Actor).HasMaxLength(256);
        builder.Property(m => m.CorrelationId).HasMaxLength(64);
        builder.Property(m => m.LastError).HasMaxLength(OutboxMessage.ErrorMaxLength);

        builder.HasIndex(m => m.MessageId).IsUnique();

        // Narrow index for the dispatcher's polling query; processed rows drop out of it.
        builder.HasIndex(m => new { m.NextAttemptAt, m.Id })
            .HasFilter("[ProcessedAt] IS NULL AND [DeadLetteredAt] IS NULL")
            .HasDatabaseName("IX_OutboxMessages_Pending");
    }
}

internal sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        builder.ToTable("InboxMessages", Schemas.Messaging);
        builder.HasKey(m => new { m.MessageId, m.Consumer });
        builder.Property(m => m.Consumer).HasMaxLength(100).IsUnicode(false);
        builder.Property(m => m.Type).HasMaxLength(100).IsUnicode(false);
    }
}

internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.ToTable("Users", Schemas.Identity);
        builder.Property(u => u.DisplayName).HasMaxLength(100).IsRequired();
    }
}

internal sealed class IdentityTablesConfiguration :
    IEntityTypeConfiguration<IdentityRole>,
    IEntityTypeConfiguration<IdentityUserRole<string>>,
    IEntityTypeConfiguration<IdentityUserClaim<string>>,
    IEntityTypeConfiguration<IdentityUserLogin<string>>,
    IEntityTypeConfiguration<IdentityUserToken<string>>,
    IEntityTypeConfiguration<IdentityRoleClaim<string>>
{
    public void Configure(EntityTypeBuilder<IdentityRole> builder) => builder.ToTable("Roles", Schemas.Identity);

    public void Configure(EntityTypeBuilder<IdentityUserRole<string>> builder) => builder.ToTable("UserRoles", Schemas.Identity);

    public void Configure(EntityTypeBuilder<IdentityUserClaim<string>> builder) => builder.ToTable("UserClaims", Schemas.Identity);

    public void Configure(EntityTypeBuilder<IdentityUserLogin<string>> builder) => builder.ToTable("UserLogins", Schemas.Identity);

    public void Configure(EntityTypeBuilder<IdentityUserToken<string>> builder) => builder.ToTable("UserTokens", Schemas.Identity);

    public void Configure(EntityTypeBuilder<IdentityRoleClaim<string>> builder) => builder.ToTable("RoleClaims", Schemas.Identity);
}

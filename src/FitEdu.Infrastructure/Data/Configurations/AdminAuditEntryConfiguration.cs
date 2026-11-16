using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using FitEdu.Domain.Entities;

namespace FitEdu.Infrastructure.Data.Configurations;

public class AdminAuditEntryConfiguration : IEntityTypeConfiguration<AdminAuditEntry>
{
    public void Configure(EntityTypeBuilder<AdminAuditEntry> builder)
    {
        builder.HasKey(e => e.Id);

        // Ids of AspNetUsers rows but deliberately no foreign keys: the log must survive the deletion
        // of the member it names (and of an admin), and must never block or cascade that deletion.
        builder.Property(e => e.ActorUserId)
            .IsRequired()
            .HasMaxLength(450);

        builder.Property(e => e.TargetUserId)
            .IsRequired()
            .HasMaxLength(450);

        builder.Property(e => e.Action)
            .HasConversion<int>();

        builder.Property(e => e.Detail)
            .HasMaxLength(AdminAuditEntry.DetailMaxLength);

        // The page lists newest first; the member filter reads one member's rows newest first.
        builder.HasIndex(e => e.OccurredAt)
            .HasDatabaseName("IX_AdminAuditEntries_OccurredAt");

        builder.HasIndex(e => new { e.TargetUserId, e.OccurredAt })
            .HasDatabaseName("IX_AdminAuditEntries_TargetUserId_OccurredAt");
    }
}

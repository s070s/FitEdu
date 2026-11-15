using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using FitEdu.Domain.Entities;

namespace FitEdu.Infrastructure.Data.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.UserId)
            .IsRequired();

        builder.Property(t => t.TokenHash)
            .IsRequired()
            .HasMaxLength(500);

        // Two requests rotating the same token at once: the second save finds RevokedAt already set and
        // fails with a concurrency exception instead of issuing a second live token pair (REL-8).
        builder.Property(t => t.RevokedAt)
            .IsConcurrencyToken();

        builder.HasIndex(t => t.UserId);
        builder.HasIndex(t => t.TokenHash);

        builder.HasOne(t => t.User)
            .WithMany(u => u.RefreshTokens)
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

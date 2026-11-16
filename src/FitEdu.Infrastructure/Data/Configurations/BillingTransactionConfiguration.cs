using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using FitEdu.Domain.Entities;

namespace FitEdu.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core configuration for BillingTransaction entity.
/// </summary>
public class BillingTransactionConfiguration : IEntityTypeConfiguration<BillingTransaction>
{
    public void Configure(EntityTypeBuilder<BillingTransaction> builder)
    {
        builder.HasKey(bt => bt.Id);

        builder.Property(bt => bt.Amount)
            .HasPrecision(18, 2);

        builder.Property(bt => bt.Currency)
            .HasMaxLength(3)
            .HasDefaultValue("EUR");

        builder.Property(bt => bt.Type)
            .HasConversion<int>();

        builder.Property(bt => bt.Description)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(bt => bt.StripeSessionId)
            .HasMaxLength(200);

        // Billing records are tax records kept for the retention period (BillingRetention), so deleting
        // the user detaches them (UserId set to null by the database) instead of deleting them.
        builder.HasOne(bt => bt.User)
            .WithMany()
            .HasForeignKey(bt => bt.UserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(bt => bt.SubscriptionPlan)
            .WithMany()
            .HasForeignKey(bt => bt.SubscriptionPlanId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(bt => bt.UserId);
        builder.HasIndex(bt => bt.CreatedAt);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using FitEdu.Domain.Entities;

namespace FitEdu.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core configuration for Course entity.
/// </summary>
public class CourseConfiguration : IEntityTypeConfiguration<Course>
{
    public void Configure(EntityTypeBuilder<Course> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.TitleEn)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(c => c.TitleEl)
            .HasMaxLength(200);

        builder.Property(c => c.DescriptionEn)
            .HasMaxLength(2000);

        builder.Property(c => c.DescriptionEl)
            .HasMaxLength(2000);

        // RequiredTier stored as int
        builder.Property(c => c.RequiredTier)
            .HasConversion<int>();

        // Level and Goal stored as int
        builder.Property(c => c.Level)
            .HasConversion<int>();

        builder.Property(c => c.Goal)
            .HasConversion<int>();

        builder.Property(c => c.Equipment)
            .HasMaxLength(120);

        builder.Property(c => c.CoverExerciseSlug)
            .HasMaxLength(120);

        // Index on IsPublished for catalog queries
        builder.HasIndex(c => c.IsPublished);

        // Index for the catalog's level and goal filters
        builder.HasIndex(c => new { c.Level, c.Goal });

        // Relationship: Course optionally belongs to one Category
        builder.HasOne(c => c.Category)
            .WithMany(cat => cat.Courses)
            .HasForeignKey(c => c.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(c => c.CategoryId);

        // Relationships configured in ModuleConfiguration (dependent side)
    }
}

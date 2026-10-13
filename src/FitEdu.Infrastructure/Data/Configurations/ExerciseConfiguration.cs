using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using FitEdu.Domain.Entities;

namespace FitEdu.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core configuration for Exercise entity (the exercise library).
/// </summary>
public class ExerciseConfiguration : IEntityTypeConfiguration<Exercise>
{
    public void Configure(EntityTypeBuilder<Exercise> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Slug)
            .IsRequired()
            .HasMaxLength(120);

        builder.Property(e => e.NameEn)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(e => e.NameEl)
            .HasMaxLength(150);

        builder.Property(e => e.Category)
            .IsRequired()
            .HasMaxLength(40);

        builder.Property(e => e.Level)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(e => e.Force)
            .HasMaxLength(20);

        builder.Property(e => e.Mechanic)
            .HasMaxLength(20);

        builder.Property(e => e.Equipment)
            .HasMaxLength(40);

        builder.Property(e => e.PrimaryMuscles)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.SecondaryMuscles)
            .IsRequired()
            .HasMaxLength(300);

        builder.Property(e => e.InstructionsEn)
            .IsRequired()
            .HasMaxLength(4000);

        builder.Property(e => e.InstructionsEl)
            .HasMaxLength(4000);

        // The slug is the public key (URL, image folder, seed JSON), so it must be unique.
        builder.HasIndex(e => e.Slug)
            .IsUnique()
            .HasDatabaseName("IX_Exercises_Slug");

        // Index for the library page's category and level filters
        builder.HasIndex(e => new { e.Category, e.Level });

        // Relationships configured in LessonExerciseConfiguration (dependent side)
    }
}

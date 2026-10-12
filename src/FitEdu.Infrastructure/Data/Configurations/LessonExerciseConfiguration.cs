using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using FitEdu.Domain.Entities;

namespace FitEdu.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core configuration for LessonExercise (an exercise prescribed inside a lesson).
/// </summary>
public class LessonExerciseConfiguration : IEntityTypeConfiguration<LessonExercise>
{
    public void Configure(EntityTypeBuilder<LessonExercise> builder)
    {
        builder.HasKey(le => le.Id);

        builder.Property(le => le.Reps)
            .HasMaxLength(40);

        builder.Property(le => le.NotesEn)
            .HasMaxLength(500);

        builder.Property(le => le.NotesEl)
            .HasMaxLength(500);

        // Relationship: a prescription belongs to exactly one Lesson and goes with it
        builder.HasOne(le => le.Lesson)
            .WithMany(l => l.Exercises)
            .HasForeignKey(le => le.LessonId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict: deleting library data must never silently strip a lesson's prescription
        builder.HasOne(le => le.Exercise)
            .WithMany(e => e.LessonExercises)
            .HasForeignKey(le => le.ExerciseId)
            .OnDelete(DeleteBehavior.Restrict);

        // Index for ordered retrieval within a lesson
        builder.HasIndex(le => new { le.LessonId, le.SortOrder });
    }
}

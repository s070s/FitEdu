using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using FitEdu.Domain.Entities;

namespace FitEdu.Application.Data;

/// <summary>
/// The application's data sets as the Application services use them. It exists so a test can swap the
/// context and so the services can be built by dependency injection; it does not make the services
/// independent of the data provider: the DbSets are EF Core types, the services write LINQ to
/// Entities queries against them, and the identity tables below are part of the surface.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<Course> Courses { get; }
    DbSet<Category> Categories { get; }
    DbSet<Module> Modules { get; }
    DbSet<Lesson> Lessons { get; }
    DbSet<Exercise> Exercises { get; }
    DbSet<LessonExercise> LessonExercises { get; }
    DbSet<AssessmentDefinition> AssessmentDefinitions { get; }
    DbSet<AssessmentSubmission> AssessmentSubmissions { get; }
    DbSet<Enrollment> Enrollments { get; }
    DbSet<SubscriptionPlan> SubscriptionPlans { get; }
    DbSet<UserSubscription> UserSubscriptions { get; }
    DbSet<LessonCompletion> LessonCompletions { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<BillingTransaction> BillingTransactions { get; }
    DbSet<SiteSetting> SiteSettings { get; }
    DbSet<Certificate> Certificates { get; }
    DbSet<ChatConversation> ChatConversations { get; }
    DbSet<ChatMessage> ChatMessages { get; }
    DbSet<CallSession> CallSessions { get; }
    DbSet<CallParticipant> CallParticipants { get; }
    DbSet<BlogArticle> BlogArticles { get; }
    DbSet<Testimonial> Testimonials { get; }
    DbSet<AssistantContentChunk> AssistantContentChunks { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<CourseReview> CourseReviews { get; }
    DbSet<LearningPath> LearningPaths { get; }
    DbSet<LearningPathStep> LearningPathSteps { get; }
    DbSet<ScheduledSession> ScheduledSessions { get; }
    DbSet<SessionRegistration> SessionRegistrations { get; }
    DbSet<AdminAuditEntry> AdminAuditEntries { get; }

    // Identity role tables for the role lookups that join users to their role names (AdminUserService,
    // CallQueryService, ChatQueryService, LandingService). Role changes go through UserManager, which
    // other Application services use directly.
    DbSet<IdentityUserRole<string>> UserRoles { get; }
    DbSet<IdentityRole> Roles { get; }

    /// <summary>The change-tracker entry of an entity, for re-reading a row another writer changed.</summary>
    EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

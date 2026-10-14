using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Entities;
using FitEdu.Domain.Enums;
using FitEdu.Domain.Identity;
using FitEdu.Infrastructure.Data;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

public class ProfileIntegrationTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public ProfileIntegrationTests(CustomWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task GetProfile_Anonymous_Returns401()
    {
        var client = _factory.CreateClient();

        (await client.GetAsync("/api/profile")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetProfile_Authenticated_Returns200WithEmail()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Student");

        var response = await client.GetAsync("/api/profile");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<ProfileDto>();
        dto!.Email.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task UpdateProfile_ChangesName()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Student");

        var response = await client.PutAsJsonAsync("/api/profile",
            new UpdateProfileRequest("UpdatedFirst", "UpdatedLast", "Nick", null, UserStatus.Intermediate));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<ProfileDto>();
        dto!.FirstName.ShouldBe("UpdatedFirst");
        dto.DisplayName.ShouldBe("Nick");
        dto.Status.ShouldBe(UserStatus.Intermediate);
    }

    [Fact]
    public async Task UpdateProfile_SavesTheGoalAndANullGoalClearsIt()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Student");

        var saved = await (await client.PutAsJsonAsync("/api/profile",
            new UpdateProfileRequest("Goal", "Setter", null, null, UserStatus.Advanced, FitnessGoal.Endurance)))
            .Content.ReadFromJsonAsync<ProfileDto>();
        var reloaded = await client.GetFromJsonAsync<ProfileDto>("/api/profile");
        var cleared = await (await client.PutAsJsonAsync("/api/profile",
            new UpdateProfileRequest("Goal", "Setter", null, null, UserStatus.Advanced, Goal: null)))
            .Content.ReadFromJsonAsync<ProfileDto>();

        saved!.Goal.ShouldBe(FitnessGoal.Endurance);
        reloaded!.Goal.ShouldBe(FitnessGoal.Endurance);
        reloaded.Status.ShouldBe(UserStatus.Advanced);
        cleared!.Goal.ShouldBeNull();
    }

    [Fact]
    public async Task UpdateProfile_UnknownGoalValue_Returns400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Student");

        var response = await client.PutAsJsonAsync("/api/profile",
            new UpdateProfileRequest("Bad", "Goal", null, null, UserStatus.Beginner, (FitnessGoal)42));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateProfile_SavesThePreferredLanguageAndANullLanguageKeepsIt()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Student");

        var before = await client.GetFromJsonAsync<ProfileDto>("/api/profile");
        var saved = await (await client.PutAsJsonAsync("/api/profile",
            new UpdateProfileRequest("Lang", "Setter", null, null, UserStatus.Beginner, PreferredLanguage: UserLanguage.Greek)))
            .Content.ReadFromJsonAsync<ProfileDto>();
        var reloaded = await client.GetFromJsonAsync<ProfileDto>("/api/profile");
        var keptByNull = await (await client.PutAsJsonAsync("/api/profile",
            new UpdateProfileRequest("Lang", "Setter", null, null, UserStatus.Beginner)))
            .Content.ReadFromJsonAsync<ProfileDto>();

        before!.PreferredLanguage.ShouldBe(UserLanguage.English);
        saved!.PreferredLanguage.ShouldBe(UserLanguage.Greek);
        reloaded!.PreferredLanguage.ShouldBe(UserLanguage.Greek);
        keptByNull!.PreferredLanguage.ShouldBe(UserLanguage.Greek);
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("EL")]
    [InlineData("el-GR")]
    public async Task UpdateProfile_UnsupportedLanguage_Returns400(string language)
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Student");

        var response = await client.PutAsJsonAsync("/api/profile",
            new UpdateProfileRequest("Bad", "Language", null, null, UserStatus.Beginner, PreferredLanguage: language));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ExportMyData_IncludesThePreferredLanguage()
    {
        var (client, userId) = await _factory.CreateAuthenticatedClientWithIdAsync("Student");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.SingleAsync(u => u.Id == userId);
            user.PreferredLanguage = UserLanguage.Greek;
            await db.SaveChangesAsync();
        }

        var export = await client.GetFromJsonAsync<MyDataExportDto>("/api/profile/export");

        export!.Profile.PreferredLanguage.ShouldBe(UserLanguage.Greek);
    }

    [Fact]
    public async Task ChangePassword_ExceedsPerUserRateLimit_Returns429()
    {
        // SEC-3: change-password had no back-pressure at all. The limiter counts every request
        // regardless of body validity, so a wrong CurrentPassword still exhausts the budget.
        var client = await _factory.CreateAuthenticatedClientAsync("Student");
        var body = new ChangePasswordRequest("wrong-current-password", "New-Pass-1!");

        HttpResponseMessage? last = null;
        for (var i = 0; i < 21; i++)
            last = await client.PostAsJsonAsync("/api/profile/change-password", body);

        last!.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task DeleteAccount_Anonymous_Returns401()
    {
        var client = _factory.CreateClient();

        (await client.DeleteAsync("/api/profile")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteAccount_AuthenticatedStudent_RemovesUser()
    {
        // COMP-3: self-service erasure — the endpoint reuses AdminUserService.DeleteUserAsync,
        // so this only needs to confirm the route/auth wiring, not the deletion logic itself
        // (covered by AdminUserServiceTests).
        var (client, _) = await _factory.CreateAuthenticatedClientWithIdAsync("Student");

        var response = await client.DeleteAsync("/api/profile");

        // API-8: DELETE returns 204, matching every other DELETE in the codebase.
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetAsync("/api/profile")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteAccount_Admin_Returns400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Admin");

        var response = await client.DeleteAsync("/api/profile");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ExportMyData_Anonymous_Returns401()
    {
        var client = _factory.CreateClient();

        (await client.GetAsync("/api/profile/export")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ExportMyData_Authenticated_ReturnsDownloadableJsonWithAllSections()
    {
        // COMP-4: GDPR access/portability — one JSON file aggregating every personal-data
        // category the recommendation calls out. Seeds one row per category directly via the
        // DbContext (no admin endpoints for course/certificate creation exist in this factory)
        // and checks each maps through, not just that the endpoint responds.
        var (client, userId) = await _factory.CreateAuthenticatedClientWithIdAsync("Student");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var course = new Course { Id = Guid.NewGuid(), TitleEn = "Export Test Course", IsPublished = true };
            db.Courses.Add(course);
            var enrollment = new Enrollment
            {
                Id = Guid.NewGuid(), UserId = userId, CourseId = course.Id,
                Status = EnrollmentStatus.Completed, CompletedAt = DateTime.UtcNow
            };
            db.Enrollments.Add(enrollment);

            var assessment = new AssessmentDefinition
            {
                Id = Guid.NewGuid(), Key = $"export-test-{Guid.NewGuid():N}",
                TitleEn = "Export Test Assessment", SchemaJson = """{"questions":[]}"""
            };
            db.AssessmentDefinitions.Add(assessment);
            db.AssessmentSubmissions.Add(new AssessmentSubmission
            {
                Id = Guid.NewGuid(), UserId = userId, AssessmentDefinitionId = assessment.Id,
                AnswersJson = """{"q1":"answer"}"""
            });

            db.Certificates.Add(new Certificate
            {
                Id = Guid.NewGuid(), UserId = userId, EnrollmentId = enrollment.Id, CourseId = course.Id,
                RecipientName = "Test User", CourseTitleEn = course.TitleEn
            });

            var plan = new SubscriptionPlan { Id = Guid.NewGuid(), Name = "Pro", Tier = SubscriptionTier.Pro, Price = 20m, IsActive = true };
            db.SubscriptionPlans.Add(plan);
            db.BillingTransactions.Add(new BillingTransaction
            {
                Id = Guid.NewGuid(), UserId = userId, SubscriptionPlanId = plan.Id,
                Amount = 20m, Type = BillingTransactionType.Purchase, Description = "Test purchase"
            });

            db.ChatMessages.Add(new ChatMessage
            {
                Id = Guid.NewGuid(), ConversationId = Guid.NewGuid(), SenderId = userId, Content = "Hello export test"
            });
            // Someone else's message must NOT appear in this user's export.
            db.ChatMessages.Add(new ChatMessage
            {
                Id = Guid.NewGuid(), ConversationId = Guid.NewGuid(), SenderId = $"other-{Guid.NewGuid():N}", Content = "Not mine"
            });

            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync("/api/profile/export");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        var export = await response.Content.ReadFromJsonAsync<MyDataExportDto>();

        export!.Profile.Email.ShouldNotBeNullOrEmpty();
        export.Enrollments.Single().CourseTitle.ShouldBe("Export Test Course");
        export.AssessmentSubmissions.Single().AssessmentTitle.ShouldBe("Export Test Assessment");
        export.Certificates.Single().CourseTitle.ShouldBe("Export Test Course");
        export.BillingTransactions.Single().PlanName.ShouldBe("Pro");
        export.ChatMessages.Single().Content.ShouldBe("Hello export test");
    }

    [Fact]
    public async Task ExportMyData_IncludesTheTrainingGoal()
    {
        // The goal is personal data like the training level, so the GDPR export carries it too.
        var (client, userId) = await _factory.CreateAuthenticatedClientWithIdAsync("Student");
        await _factory.SeedAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Id == userId);
            user.Goal = FitnessGoal.Strength;
            await db.SaveChangesAsync();
        });

        var export = await client.GetFromJsonAsync<MyDataExportDto>("/api/profile/export");

        export!.Profile.Goal.ShouldBe(nameof(FitnessGoal.Strength));
    }

    [Fact]
    public async Task GetTrainingStats_Anonymous_Returns401()
    {
        var client = _factory.CreateClient();

        (await client.GetAsync("/api/profile/training-stats")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetTrainingStats_CountsTheMembersCompletedWorkouts()
    {
        var (client, userId) = await _factory.CreateAuthenticatedClientWithIdAsync("Student");
        await _factory.SeedAsync(async db =>
        {
            var course = new Course { Id = Guid.NewGuid(), TitleEn = $"Stats-{Guid.NewGuid():N}", IsPublished = true };
            var module = new Module { Id = Guid.NewGuid(), TitleEn = "Week 1", SortOrder = 1, CourseId = course.Id };
            var lesson = new Lesson { Id = Guid.NewGuid(), TitleEn = "Workout 1", SortOrder = 1, DurationMinutes = 25 };
            module.Lessons.Add(lesson);
            course.Modules.Add(module);
            db.Courses.Add(course);
            db.Enrollments.Add(new Enrollment { UserId = userId, CourseId = course.Id });
            db.LessonCompletions.Add(new LessonCompletion { UserId = userId, LessonId = lesson.Id });
            await db.SaveChangesAsync();
        });

        var stats = await client.GetFromJsonAsync<TrainingStatsDto>("/api/profile/training-stats");

        stats!.WorkoutsCompleted.ShouldBe(1);
        stats.MinutesTrained.ShouldBe(25);
        stats.CurrentStreak.ShouldBe(1);
        stats.CurrentCourseId.ShouldNotBeNull();
    }
}

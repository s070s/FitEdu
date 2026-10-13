using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.ApiServices;
using FitEdu.Domain.Entities;
using FitEdu.Domain.Enums;
using FitEdu.Domain.Identity;
using FitEdu.Infrastructure.Data;
using FitEdu.TestSupport;
using Shouldly;
using Xunit;

namespace FitEdu.Application.Tests;

/// <summary>
/// The home dashboard's training stats: completions, minutes and the daily streak, counted in UTC
/// days against a fixed clock so the result is the same on every machine and in CI.
/// </summary>
public class ProfileTrainingStatsTests
{
    private const string UserId = "member-1";
    private const int LessonMinutes = 20;

    // Tuesday 10 March 2026, 08:00 UTC.
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 8, 0, 0, TimeSpan.Zero);

    private readonly ApplicationDbContext _db = DbContextFactory.CreateInMemory();
    private readonly ApplicationUser _member = new() { Id = UserId, Email = "member@fitedu.local", FirstName = "Maria", LastName = "Papa" };

    private ProfileService NewService()
    {
        var userManager = IdentityMocks.MockUserManager();
        userManager.FindByIdAsync(UserId).Returns(_member);
        return new ProfileService(userManager, Substitute.For<IFileStorage>(), _db,
            NullLogger<ProfileService>.Instance, new FixedTimeProvider(Now));
    }

    [Fact]
    public async Task NoTraining_AllZeroAndNoProgrammeToContinue()
    {
        var stats = await NewService().GetTrainingStatsAsync(UserId);

        stats.WorkoutsCompleted.ShouldBe(0);
        stats.MinutesTrained.ShouldBe(0);
        stats.CurrentStreak.ShouldBe(0);
        stats.CurrentCourseId.ShouldBeNull();
    }

    [Fact]
    public async Task CountsCompletionsAndTheirPlannedMinutes()
    {
        var course = await AddCourseAsync(lessonCount: 3);
        await CompleteAsync(course, Now.AddDays(-10), Now.AddDays(-9), Now.AddDays(-9).AddHours(1));

        var stats = await NewService().GetTrainingStatsAsync(UserId);

        stats.WorkoutsCompleted.ShouldBe(3);
        stats.MinutesTrained.ShouldBe(3 * LessonMinutes);
    }

    [Fact]
    public async Task Streak_CountsConsecutiveDaysEndingToday()
    {
        var course = await AddCourseAsync(lessonCount: 4);
        // Today, yesterday, the day before, then a gap of one day.
        await CompleteAsync(course, Now.AddHours(-1), Now.AddDays(-1), Now.AddDays(-2), Now.AddDays(-4));

        var stats = await NewService().GetTrainingStatsAsync(UserId);

        stats.CurrentStreak.ShouldBe(3);
    }

    [Fact]
    public async Task Streak_StillCountsWhenTheLastWorkoutWasYesterday()
    {
        var course = await AddCourseAsync(lessonCount: 2);
        await CompleteAsync(course, Now.AddDays(-1), Now.AddDays(-2));

        var stats = await NewService().GetTrainingStatsAsync(UserId);

        stats.CurrentStreak.ShouldBe(2);
    }

    [Fact]
    public async Task Streak_IsBrokenAfterAWholeDayWithoutTraining()
    {
        var course = await AddCourseAsync(lessonCount: 2);
        await CompleteAsync(course, Now.AddDays(-2), Now.AddDays(-3));

        var stats = await NewService().GetTrainingStatsAsync(UserId);

        stats.CurrentStreak.ShouldBe(0);
    }

    [Fact]
    public async Task Streak_SplitsDaysAtMidnightUtc()
    {
        var course = await AddCourseAsync(lessonCount: 2);
        var lateYesterday = new DateTimeOffset(2026, 3, 9, 23, 30, 0, TimeSpan.Zero);
        var earlyToday = new DateTimeOffset(2026, 3, 10, 0, 10, 0, TimeSpan.Zero);
        await CompleteAsync(course, lateYesterday, earlyToday);

        var stats = await NewService().GetTrainingStatsAsync(UserId);

        stats.CurrentStreak.ShouldBe(2);
    }

    [Fact]
    public async Task CurrentCourse_IsTheNewestEnrolmentStillInProgress()
    {
        var olderCourse = await AddCourseAsync(lessonCount: 1);
        var newestCourse = await AddCourseAsync(lessonCount: 1);
        var finishedCourse = await AddCourseAsync(lessonCount: 1);
        await EnrolAsync(olderCourse, Now.AddDays(-20), EnrollmentStatus.Active);
        await EnrolAsync(newestCourse, Now.AddDays(-5), EnrollmentStatus.Active);
        await EnrolAsync(finishedCourse, Now.AddDays(-1), EnrollmentStatus.Completed);

        var stats = await NewService().GetTrainingStatsAsync(UserId);

        stats.CurrentCourseId.ShouldBe(newestCourse.Id);
    }

    [Theory]
    [InlineData(UserStatus.Beginner, CourseLevel.Beginner)]
    [InlineData(UserStatus.Advanced, CourseLevel.Advanced)]
    [InlineData(UserStatus.Unknown, null)]
    [InlineData(UserStatus.Other, null)]
    public async Task GoalAndLevel_AreTheMembersOwnChoices(UserStatus status, CourseLevel? expectedLevel)
    {
        _member.Status = status;
        _member.Goal = FitnessGoal.Mobility;

        var stats = await NewService().GetTrainingStatsAsync(UserId);

        stats.Goal.ShouldBe(FitnessGoal.Mobility);
        stats.Level.ShouldBe(expectedLevel);
    }

    private async Task<Course> AddCourseAsync(int lessonCount)
    {
        var course = new Course { Id = Guid.NewGuid(), TitleEn = $"Course {Guid.NewGuid():N}", IsPublished = true };
        var module = new Module { Id = Guid.NewGuid(), TitleEn = "Week 1", SortOrder = 1, CourseId = course.Id };
        for (var index = 0; index < lessonCount; index++)
        {
            module.Lessons.Add(new Lesson { Id = Guid.NewGuid(), TitleEn = $"Workout {index + 1}", SortOrder = index + 1, DurationMinutes = LessonMinutes });
        }

        course.Modules.Add(module);
        _db.Courses.Add(course);
        await _db.SaveChangesAsync();
        return course;
    }

    /// <summary>Completes the course's lessons in order, one per given moment.</summary>
    private async Task CompleteAsync(Course course, params DateTimeOffset[] completedAt)
    {
        var lessons = course.Modules.SelectMany(m => m.Lessons).OrderBy(l => l.SortOrder).ToList();
        for (var index = 0; index < completedAt.Length; index++)
        {
            _db.LessonCompletions.Add(new LessonCompletion
            {
                UserId = UserId,
                LessonId = lessons[index].Id,
                CompletedAt = completedAt[index].UtcDateTime
            });
        }

        await _db.SaveChangesAsync();
    }

    private async Task EnrolAsync(Course course, DateTimeOffset enrolledAt, EnrollmentStatus status)
    {
        _db.Enrollments.Add(new Enrollment { UserId = UserId, CourseId = course.Id, EnrolledAt = enrolledAt.UtcDateTime, Status = status });
        await _db.SaveChangesAsync();
    }
}

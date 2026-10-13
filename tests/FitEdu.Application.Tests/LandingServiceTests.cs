using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.ApiServices;
using FitEdu.Domain.Entities;
using FitEdu.Infrastructure.Data;
using FitEdu.TestSupport;
using Shouldly;
using Xunit;

namespace FitEdu.Application.Tests;

/// <summary>
/// The public home page's numbers count only published content and members, the showcase lists
/// the most-enrolled courses first, and the admin's hero photo counts only when its file exists.
/// </summary>
public class LandingServiceTests
{
    private const string HeroPath = "backgrounds/hero.webp";

    private readonly ApplicationDbContext _db = DbContextFactory.CreateInMemory();
    private readonly IFileStorage _fileStorage = Substitute.For<IFileStorage>();
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

    private LandingService NewService()
    {
        var reviews = new CourseReviewService(_db, Substitute.For<INotificationDispatcher>(), NullLogger<CourseReviewService>.Instance);
        var courses = new CourseService(_db, Substitute.For<ISubscriptionService>(), Substitute.For<ICertificateService>(),
            reviews, NullLogger<CourseService>.Instance);
        return new LandingService(_db, courses, _fileStorage, _cache);
    }

    [Fact]
    public async Task Counts_PublishedCoursesTheirLessonsExercisesAndMembers()
    {
        AddCourse("Published", published: true, lessonCount: 3);
        AddCourse("Draft", published: false, lessonCount: 5);
        _db.Exercises.Add(Exercise("squat"));
        _db.Exercises.Add(Exercise("plank"));
        _db.Exercises.Add(Exercise("draft-lunge", published: false));
        AddUserInRole("student-1", "Student");
        AddUserInRole("student-2", "Student");
        AddUserInRole("admin-1", "Admin");
        await _db.SaveChangesAsync();

        var overview = await NewService().GetOverviewAsync("en");

        overview.CourseCount.ShouldBe(1);
        overview.LessonCount.ShouldBe(3);
        overview.ExerciseCount.ShouldBe(2);
        overview.MemberCount.ShouldBe(2);
    }

    [Fact]
    public async Task FeaturedCourses_AreTheMostEnrolledPublishedOnesAndNoneReadsAsEnrolled()
    {
        var quiet = AddCourse("Alpha quiet", published: true, lessonCount: 1);
        var popular = AddCourse("Zulu popular", published: true, lessonCount: 1);
        AddCourse("Hidden", published: false, lessonCount: 1);
        _db.Enrollments.Add(new Enrollment { UserId = "student-1", CourseId = popular.Id });
        _db.Enrollments.Add(new Enrollment { UserId = "student-2", CourseId = popular.Id });
        _db.Enrollments.Add(new Enrollment { UserId = "student-1", CourseId = quiet.Id });
        await _db.SaveChangesAsync();

        var overview = await NewService().GetOverviewAsync("en");

        overview.FeaturedCourses.Select(c => c.Id).ShouldBe([popular.Id, quiet.Id]);
        overview.FeaturedCourses.ShouldAllBe(c => !c.IsEnrolled);
    }

    [Fact]
    public async Task FeaturedCourses_StopAtTheShowcaseSize()
    {
        for (var index = 0; index < LandingService.FeaturedCourseCount + 2; index++)
        {
            AddCourse($"Course {index}", published: true, lessonCount: 1);
        }

        await _db.SaveChangesAsync();

        var overview = await NewService().GetOverviewAsync("en");

        overview.FeaturedCourses.Count.ShouldBe(LandingService.FeaturedCourseCount);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task CustomHeroImage_CountsOnlyWhenTheUploadedFileExists(bool fileExists, bool expected)
    {
        _db.SiteSettings.Add(new SiteSetting { Id = Guid.NewGuid(), Key = SiteSetting.LandingBackgroundImageKey, Value = HeroPath });
        await _db.SaveChangesAsync();
        _fileStorage.FileExists(HeroPath).Returns(fileExists);

        var overview = await NewService().GetOverviewAsync("en");

        overview.HasCustomHeroImage.ShouldBe(expected);
    }

    [Fact]
    public async Task Overview_IsServedFromTheCachePerLanguage()
    {
        AddCourse("First", published: true, lessonCount: 1);
        await _db.SaveChangesAsync();
        var service = NewService();
        var firstEnglish = await service.GetOverviewAsync("en");

        AddCourse("Second", published: true, lessonCount: 1);
        await _db.SaveChangesAsync();
        var cachedEnglish = await service.GetOverviewAsync("en");
        var freshGreek = await service.GetOverviewAsync("el");

        firstEnglish.CourseCount.ShouldBe(1);
        cachedEnglish.CourseCount.ShouldBe(1);
        freshGreek.CourseCount.ShouldBe(2);
    }

    [Fact]
    public async Task CustomHeroImage_IsOffWhenNoneWasUploaded()
    {
        var overview = await NewService().GetOverviewAsync("en");

        overview.HasCustomHeroImage.ShouldBeFalse();
    }

    private Course AddCourse(string title, bool published, int lessonCount)
    {
        var course = new Course { Id = Guid.NewGuid(), TitleEn = title, IsPublished = published };
        var module = new Module { Id = Guid.NewGuid(), TitleEn = "Week 1", SortOrder = 1, CourseId = course.Id };
        for (var index = 0; index < lessonCount; index++)
        {
            module.Lessons.Add(new Lesson { Id = Guid.NewGuid(), TitleEn = $"Workout {index + 1}", SortOrder = index + 1 });
        }

        course.Modules.Add(module);
        _db.Courses.Add(course);
        return course;
    }

    private static Exercise Exercise(string slug, bool published = true) => new()
    {
        IsPublished = published,
        Slug = slug,
        NameEn = slug,
        Category = "strength",
        Level = "beginner",
        PrimaryMuscles = "quadriceps",
        SecondaryMuscles = "",
        InstructionsEn = "Stand tall."
    };

    private void AddUserInRole(string userId, string roleName)
    {
        var role = _db.Roles.Local.FirstOrDefault(r => r.Name == roleName);
        if (role is null)
        {
            role = new IdentityRole(roleName);
            _db.Roles.Add(role);
        }

        _db.UserRoles.Add(new IdentityUserRole<string> { UserId = userId, RoleId = role.Id });
    }
}

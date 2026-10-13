using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using FitEdu.Application.DTOs;
using FitEdu.TestSupport;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.ApiServices;
using FitEdu.Infrastructure.Data;
using FitEdu.Domain.Entities;
using FitEdu.Domain.Enums;
using Shouldly;
using Xunit;

namespace FitEdu.Application.Tests;

public class CourseServiceTests
{
    private const string UserId = "user-1";

    private static UserSubscriptionStatusDto Status(
        SubscriptionTier tier = SubscriptionTier.Free,
        int maxCourses = int.MaxValue,
        bool certificateAccess = false) =>
        new(tier, tier.ToString(), DateTime.UtcNow, null, true,
            new PlanFeaturesDto { MaxCourses = maxCourses, CertificateAccess = certificateAccess });

    private static (CourseService svc, ISubscriptionService subs, ICertificateService certs) NewService(
        ApplicationDbContext db, UserSubscriptionStatusDto? status = null)
    {
        var subs = Substitute.For<ISubscriptionService>();
        subs.GetUserStatusAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(status ?? Status());
        var certs = Substitute.For<ICertificateService>();
        var reviews = new CourseReviewService(db, Substitute.For<INotificationDispatcher>(), NullLogger<CourseReviewService>.Instance);
        var svc = new CourseService(db, subs, certs, reviews, NullLogger<CourseService>.Instance);
        return (svc, subs, certs);
    }

    private static Course CourseWithLessons(
        string titleEn, int lessonCount, bool published = true,
        SubscriptionTier tier = SubscriptionTier.Free, string? titleEl = null)
    {
        var course = new Course
        {
            Id = Guid.NewGuid(),
            TitleEn = titleEn,
            TitleEl = titleEl,
            IsPublished = published,
            RequiredTier = tier
        };
        var module = new Module { Id = Guid.NewGuid(), TitleEn = "M1", SortOrder = 1, CourseId = course.Id };
        for (var i = 0; i < lessonCount; i++)
        {
            module.Lessons.Add(new Lesson
            {
                Id = Guid.NewGuid(),
                TitleEn = $"L{i + 1}",
                SortOrder = i + 1,
                DurationMinutes = 10,
                ContentEn = $"content-{i + 1}"
            });
        }
        course.Modules.Add(module);
        return course;
    }

    // ---- GetPublishedCoursesAsync -------------------------------------------

    [Fact]
    public async Task GetPublishedCourses_ReturnsOnlyPublished_OrderedByTitle()
    {
        await using var db = DbContextFactory.CreateInMemory();
        db.Courses.Add(CourseWithLessons("Beta", 0));
        db.Courses.Add(CourseWithLessons("Alpha", 0));
        db.Courses.Add(CourseWithLessons("Hidden", 0, published: false));
        await db.SaveChangesAsync();
        var (svc, _, _) = NewService(db);

        var result = await svc.GetPublishedCoursesAsync(UserId, 1, 10, "en");

        result.TotalCount.ShouldBe(2);
        result.Items.Select(i => i.Title).ShouldBe(new[] { "Alpha", "Beta" });
    }

    [Fact]
    public async Task GetPublishedCourses_SetsEnrolledFlagAndLessonCount()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var course = CourseWithLessons("Course", 3);
        db.Courses.Add(course);
        db.Enrollments.Add(new Enrollment { Id = Guid.NewGuid(), UserId = UserId, CourseId = course.Id });
        await db.SaveChangesAsync();
        var (svc, _, _) = NewService(db);

        var item = (await svc.GetPublishedCoursesAsync(UserId, 1, 10, "en")).Items.Single();

        item.IsEnrolled.ShouldBeTrue();
        item.TotalLessons.ShouldBe(3);
    }

    [Fact]
    public async Task GetPublishedCourses_GreekFallsBackToEnglishWhenElNull()
    {
        await using var db = DbContextFactory.CreateInMemory();
        db.Courses.Add(CourseWithLessons("EnTitle", 0, titleEl: null));
        await db.SaveChangesAsync();
        var (svc, _, _) = NewService(db);

        (await svc.GetPublishedCoursesAsync(UserId, 1, 10, "el")).Items.Single().Title.ShouldBe("EnTitle");
    }

    [Fact]
    public async Task GetPublishedCourses_PaginatesWithCorrectTotal()
    {
        await using var db = DbContextFactory.CreateInMemory();
        db.Courses.Add(CourseWithLessons("A", 0));
        db.Courses.Add(CourseWithLessons("B", 0));
        db.Courses.Add(CourseWithLessons("C", 0));
        await db.SaveChangesAsync();
        var (svc, _, _) = NewService(db);

        var page2 = await svc.GetPublishedCoursesAsync(UserId, 2, 1, "en");

        page2.TotalCount.ShouldBe(3);
        page2.Items.Single().Title.ShouldBe("B");
    }

    [Fact]
    public async Task GetPublishedCourses_FiltersByCategoryId()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var category = new Category { Id = Guid.NewGuid(), NameEn = "Career" };
        db.Categories.Add(category);
        var matching = CourseWithLessons("Matching", 0);
        matching.CategoryId = category.Id;
        db.Courses.Add(matching);
        db.Courses.Add(CourseWithLessons("Other", 0));
        await db.SaveChangesAsync();
        var (svc, _, _) = NewService(db);

        var result = await svc.GetPublishedCoursesAsync(UserId, 1, 10, "en", categoryId: category.Id);

        result.TotalCount.ShouldBe(1);
        result.Items.Single().Title.ShouldBe("Matching");
        result.Items.Single().CategoryName.ShouldBe("Career");
    }

    [Fact]
    public async Task GetPublishedCourses_FiltersBySearchAcrossTitleAndDescription_OnSqlite()
    {
        // EF.Functions.Like is unsupported on the InMemory provider — use the relational SQLite fixture.
        await using var db = DbContextFactory.CreateSqlite();
        var withMatchInTitle = CourseWithLessons("Financial Planning", 0);
        var withMatchInDescription = CourseWithLessons("Other", 0);
        withMatchInDescription.DescriptionEn = "Covers financial basics";
        var noMatch = CourseWithLessons("Unrelated", 0);
        db.Courses.AddRange(withMatchInTitle, withMatchInDescription, noMatch);
        await db.SaveChangesAsync();
        var (svc, _, _) = NewService(db);

        var result = await svc.GetPublishedCoursesAsync(UserId, 1, 10, "en", search: "financial");

        result.TotalCount.ShouldBe(2);
        result.Items.Select(i => i.Title).ShouldBe(new[] { "Financial Planning", "Other" }, ignoreOrder: true);
    }

    [Fact]
    public async Task GetPublishedCourses_UncategorizedCourse_HasNullCategoryName()
    {
        await using var db = DbContextFactory.CreateInMemory();
        db.Courses.Add(CourseWithLessons("Course", 0));
        await db.SaveChangesAsync();
        var (svc, _, _) = NewService(db);

        var item = (await svc.GetPublishedCoursesAsync(UserId, 1, 10, "en")).Items.Single();

        item.CategoryId.ShouldBeNull();
        item.CategoryName.ShouldBeNull();
    }

    private static Course FitnessCourse(string titleEn, FitnessGoal goal, CourseLevel level, string? equipment)
    {
        var course = CourseWithLessons(titleEn, 0);
        course.Goal = goal;
        course.Level = level;
        course.Equipment = equipment;
        return course;
    }

    [Fact]
    public async Task GetPublishedCourses_CarriesFitnessFieldsAndCoverUrl()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var course = FitnessCourse("Course", FitnessGoal.FatLoss, CourseLevel.Intermediate, "none,bands");
        course.EstimatedWeeks = 6;
        course.SessionsPerWeek = 3;
        course.CoverExerciseSlug = "plank";
        db.Courses.AddRange(course, CourseWithLessons("No Cover", 0));
        await db.SaveChangesAsync();
        var (svc, _, _) = NewService(db);

        var items = (await svc.GetPublishedCoursesAsync(UserId, 1, 10, "en")).Items;

        var item = items.Single(i => i.Title == "Course");
        item.Goal.ShouldBe(FitnessGoal.FatLoss);
        item.Level.ShouldBe(CourseLevel.Intermediate);
        item.Equipment.ShouldBe("none,bands");
        item.EstimatedWeeks.ShouldBe(6);
        item.SessionsPerWeek.ShouldBe(3);
        item.CoverImageUrl.ShouldBe("/images/exercises/plank/0.webp");
        items.Single(i => i.Title == "No Cover").CoverImageUrl.ShouldBeNull();
    }

    [Fact]
    public async Task GetPublishedCourses_FiltersByGoalLevelAndEquipment()
    {
        await using var db = DbContextFactory.CreateInMemory();
        db.Courses.AddRange(
            FitnessCourse("Match", FitnessGoal.Strength, CourseLevel.Advanced, "barbell,dumbbells,cables"),
            FitnessCourse("Wrong Goal", FitnessGoal.Mobility, CourseLevel.Advanced, "dumbbells"),
            FitnessCourse("Wrong Level", FitnessGoal.Strength, CourseLevel.Beginner, "dumbbells"),
            FitnessCourse("No Dumbbells", FitnessGoal.Strength, CourseLevel.Advanced, "barbell"),
            FitnessCourse("No Equipment Listed", FitnessGoal.Strength, CourseLevel.Advanced, null));
        await db.SaveChangesAsync();
        var (svc, _, _) = NewService(db);

        var result = await svc.GetPublishedCoursesAsync(
            UserId, 1, 10, "en", goal: FitnessGoal.Strength, level: CourseLevel.Advanced, equipment: "dumbbells");

        result.Items.ShouldHaveSingleItem().Title.ShouldBe("Match");
    }

    [Fact]
    public async Task GetPublishedCourses_EquipmentFilterMatchesWholeCodesInAnyPosition()
    {
        await using var db = DbContextFactory.CreateInMemory();
        db.Courses.AddRange(
            FitnessCourse("First", FitnessGoal.Strength, CourseLevel.Beginner, "kettlebell,mat"),
            FitnessCourse("Last", FitnessGoal.Strength, CourseLevel.Beginner, "mat,kettlebell"),
            FitnessCourse("Only", FitnessGoal.Strength, CourseLevel.Beginner, "kettlebell"),
            FitnessCourse("Longer Code", FitnessGoal.Strength, CourseLevel.Beginner, "kettlebells"));
        await db.SaveChangesAsync();
        var (svc, _, _) = NewService(db);

        var result = await svc.GetPublishedCoursesAsync(UserId, 1, 10, "en", equipment: "kettlebell");

        string.Join("|", result.Items.Select(i => i.Title)).ShouldBe("First|Last|Only");
    }

    [Fact]
    public async Task GetPublishedCourses_EquipmentFilterMatchesWholeCodes_OnSqlite()
    {
        // Same predicate as above, but translated to SQL by a relational provider.
        await using var db = DbContextFactory.CreateSqlite();
        db.Courses.AddRange(
            FitnessCourse("First", FitnessGoal.Strength, CourseLevel.Beginner, "kettlebell,mat"),
            FitnessCourse("Last", FitnessGoal.Strength, CourseLevel.Beginner, "mat,kettlebell"),
            FitnessCourse("Only", FitnessGoal.Strength, CourseLevel.Beginner, "kettlebell"),
            FitnessCourse("Longer Code", FitnessGoal.Strength, CourseLevel.Beginner, "kettlebells"),
            FitnessCourse("No Equipment Listed", FitnessGoal.Strength, CourseLevel.Beginner, null));
        await db.SaveChangesAsync();
        var (svc, _, _) = NewService(db);

        var result = await svc.GetPublishedCoursesAsync(UserId, 1, 10, "en", equipment: "kettlebell");

        string.Join("|", result.Items.Select(i => i.Title)).ShouldBe("First|Last|Only");
    }

    // ---- GetCourseDetailAsync -----------------------------------------------

    [Fact]
    public async Task GetCourseDetail_CarriesFitnessFieldsAndCoverUrl()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var course = FitnessCourse("Course", FitnessGoal.Endurance, CourseLevel.Beginner, "bike");
        course.EstimatedWeeks = 8;
        course.SessionsPerWeek = 4;
        course.CoverExerciseSlug = "bicycling";
        db.Courses.Add(course);
        await db.SaveChangesAsync();
        var (svc, _, _) = NewService(db);

        var detail = (await svc.GetCourseDetailAsync(UserId, course.Id, "en"))!;

        detail.Goal.ShouldBe(FitnessGoal.Endurance);
        detail.Level.ShouldBe(CourseLevel.Beginner);
        detail.Equipment.ShouldBe("bike");
        detail.EstimatedWeeks.ShouldBe(8);
        detail.SessionsPerWeek.ShouldBe(4);
        detail.CoverImageUrl.ShouldBe("/images/exercises/bicycling/0.webp");
    }

    [Fact]
    public async Task GetCourseDetail_Missing_ReturnsNull()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var (svc, _, _) = NewService(db);

        (await svc.GetCourseDetailAsync(UserId, Guid.NewGuid(), "en")).ShouldBeNull();
    }

    [Fact]
    public async Task GetCourseDetail_Unpublished_ReturnsNull()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var course = CourseWithLessons("C", 2, published: false);
        db.Courses.Add(course);
        await db.SaveChangesAsync();
        var (svc, _, _) = NewService(db);

        (await svc.GetCourseDetailAsync(UserId, course.Id, "en")).ShouldBeNull();
    }

    [Fact]
    public async Task GetCourseDetail_ComputesProgressPercent()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var course = CourseWithLessons("C", 4);
        db.Courses.Add(course);
        db.Enrollments.Add(new Enrollment { Id = Guid.NewGuid(), UserId = UserId, CourseId = course.Id });
        var firstLesson = course.Modules.First().Lessons.First();
        db.LessonCompletions.Add(new LessonCompletion { Id = Guid.NewGuid(), UserId = UserId, LessonId = firstLesson.Id });
        await db.SaveChangesAsync();
        var (svc, _, _) = NewService(db);

        var detail = await svc.GetCourseDetailAsync(UserId, course.Id, "en");

        detail!.TotalLessons.ShouldBe(4);
        detail.CompletedLessons.ShouldBe(1);
        detail.ProgressPercent.ShouldBe(25.0);
        detail.IsEnrolled.ShouldBeTrue();
    }

    [Fact]
    public async Task GetCourseDetail_NoLessons_ProgressIsZero()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var course = CourseWithLessons("C", 0);
        db.Courses.Add(course);
        await db.SaveChangesAsync();
        var (svc, _, _) = NewService(db);

        (await svc.GetCourseDetailAsync(UserId, course.Id, "en"))!.ProgressPercent.ShouldBe(0);
    }

    // ---- EnrollAsync ---------------------------------------------------------

    [Fact]
    public async Task Enroll_CourseMissing_ReturnsNotFound()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var (svc, _, _) = NewService(db);

        var result = await svc.EnrollAsync(UserId, Guid.NewGuid());

        result.StatusCode.ShouldBe(404);
    }

    [Fact]
    public async Task Enroll_TierBelowRequired_ReturnsForbidden()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var course = CourseWithLessons("Pro", 0, tier: SubscriptionTier.Pro);
        db.Courses.Add(course);
        await db.SaveChangesAsync();
        var (svc, _, _) = NewService(db, Status(SubscriptionTier.Free));

        (await svc.EnrollAsync(UserId, course.Id)).StatusCode.ShouldBe(403);
    }

    [Fact]
    public async Task Enroll_AtMaxCoursesCap_ReturnsForbidden()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var existing = CourseWithLessons("Existing", 0);
        var target = CourseWithLessons("Target", 0);
        db.Courses.AddRange(existing, target);
        db.Enrollments.Add(new Enrollment { Id = Guid.NewGuid(), UserId = UserId, CourseId = existing.Id });
        await db.SaveChangesAsync();
        var (svc, _, _) = NewService(db, Status(SubscriptionTier.Free, maxCourses: 1));

        (await svc.EnrollAsync(UserId, target.Id)).StatusCode.ShouldBe(403);
    }

    [Fact]
    public async Task Enroll_UnlimitedPlan_BypassesCap()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var existing = CourseWithLessons("Existing", 0);
        var target = CourseWithLessons("Target", 0);
        db.Courses.AddRange(existing, target);
        db.Enrollments.Add(new Enrollment { Id = Guid.NewGuid(), UserId = UserId, CourseId = existing.Id });
        await db.SaveChangesAsync();
        var (svc, _, _) = NewService(db, Status(maxCourses: int.MaxValue));

        (await svc.EnrollAsync(UserId, target.Id)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Enroll_AlreadyEnrolled_ReturnsOkWithoutDuplicate()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var course = CourseWithLessons("C", 0);
        db.Courses.Add(course);
        db.Enrollments.Add(new Enrollment { Id = Guid.NewGuid(), UserId = UserId, CourseId = course.Id });
        await db.SaveChangesAsync();
        var (svc, _, _) = NewService(db);

        var result = await svc.EnrollAsync(UserId, course.Id);

        result.IsSuccess.ShouldBeTrue();
        (await db.Enrollments.CountAsync(e => e.UserId == UserId && e.CourseId == course.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task Enroll_NewEnrollment_PersistsActive()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var course = CourseWithLessons("C", 0);
        db.Courses.Add(course);
        await db.SaveChangesAsync();
        var (svc, _, _) = NewService(db);

        var result = await svc.EnrollAsync(UserId, course.Id);

        result.IsSuccess.ShouldBeTrue();
        var enrollment = await db.Enrollments.SingleAsync(e => e.UserId == UserId && e.CourseId == course.Id);
        enrollment.Status.ShouldBe(EnrollmentStatus.Active);
    }

    // ---- GetLessonDetailAsync ------------------------------------------------

    [Fact]
    public async Task GetLessonDetail_Missing_ReturnsNotFound()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var (svc, _, _) = NewService(db);

        (await svc.GetLessonDetailAsync(UserId, Guid.NewGuid(), "en")).StatusCode.ShouldBe(404);
    }

    [Fact]
    public async Task GetLessonDetail_NotEnrolled_ReturnsBadRequest()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var course = CourseWithLessons("C", 2);
        db.Courses.Add(course);
        await db.SaveChangesAsync();
        var lessonId = course.Modules.First().Lessons.First().Id;
        var (svc, _, _) = NewService(db);

        (await svc.GetLessonDetailAsync(UserId, lessonId, "en")).StatusCode.ShouldBe(400);
    }

    [Fact]
    public async Task GetLessonDetail_ComputesPreviousAndNextNeighbors()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var course = CourseWithLessons("C", 3);
        db.Courses.Add(course);
        db.Enrollments.Add(new Enrollment { Id = Guid.NewGuid(), UserId = UserId, CourseId = course.Id });
        await db.SaveChangesAsync();
        var lessons = course.Modules.First().Lessons.OrderBy(l => l.SortOrder).ToList();
        var (svc, _, _) = NewService(db);

        var middle = (await svc.GetLessonDetailAsync(UserId, lessons[1].Id, "en")).Value!;
        middle.PreviousLessonId.ShouldBe(lessons[0].Id);
        middle.NextLessonId.ShouldBe(lessons[2].Id);

        var first = (await svc.GetLessonDetailAsync(UserId, lessons[0].Id, "en")).Value!;
        first.PreviousLessonId.ShouldBeNull();

        var last = (await svc.GetLessonDetailAsync(UserId, lessons[2].Id, "en")).Value!;
        last.NextLessonId.ShouldBeNull();
    }

    private static Exercise NewExercise(string slug, string nameEn) =>
        new()
        {
            Id = Guid.NewGuid(),
            Slug = slug,
            NameEn = nameEn,
            Category = "strength",
            Level = "beginner",
            PrimaryMuscles = "chest, triceps",
            SecondaryMuscles = string.Empty,
            InstructionsEn = "Lower.\nPush up.",
            IsPublished = true
        };

    [Fact]
    public async Task GetLessonDetail_IncludesPrescribedExercisesInSortOrder()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var course = CourseWithLessons("C", 1);
        var lesson = course.Modules.First().Lessons.First();
        var pushUp = NewExercise("pushups", "Pushups");
        pushUp.NameEl = "Κάμψεις";
        var squat = NewExercise("squat", "Squat");
        db.Exercises.AddRange(pushUp, squat);
        lesson.Exercises.Add(new LessonExercise { Id = Guid.NewGuid(), ExerciseId = squat.Id, SortOrder = 2, Reps = "12" });
        lesson.Exercises.Add(new LessonExercise
        {
            Id = Guid.NewGuid(), ExerciseId = pushUp.Id, SortOrder = 1,
            Sets = 3, Reps = "8-12", RestSeconds = 60, NotesEn = "Elbows at 45 degrees", NotesEl = "Αγκώνες στις 45 μοίρες"
        });
        db.Courses.Add(course);
        db.Enrollments.Add(new Enrollment { Id = Guid.NewGuid(), UserId = UserId, CourseId = course.Id });
        await db.SaveChangesAsync();
        var (svc, _, _) = NewService(db);

        var detail = (await svc.GetLessonDetailAsync(UserId, lesson.Id, "el")).Value!;

        detail.Exercises.ShouldNotBeNull();
        string.Join("|", detail.Exercises.Select(x => x.Slug)).ShouldBe("pushups|squat");
        var first = detail.Exercises[0];
        first.ExerciseId.ShouldBe(pushUp.Id);
        first.Name.ShouldBe("Κάμψεις");
        first.Sets.ShouldBe(3);
        first.Reps.ShouldBe("8-12");
        first.RestSeconds.ShouldBe(60);
        first.Notes.ShouldBe("Αγκώνες στις 45 μοίρες");
        string.Join("|", first.PrimaryMuscles).ShouldBe("chest|triceps");
        string.Join("|", first.Instructions).ShouldBe("Lower.|Push up.");
        first.Image0Url.ShouldBe("/images/exercises/pushups/0.webp");
        first.Image1Url.ShouldBe("/images/exercises/pushups/1.webp");
        detail.Exercises[1].Name.ShouldBe("Squat"); // no Greek name: English fallback
    }

    [Fact]
    public async Task GetLessonDetail_LeavesOutExercisesThatAreNotPublished()
    {
        // The exercise page of an unpublished exercise is a 404, so the lesson must not link to it.
        await using var db = DbContextFactory.CreateInMemory();
        var course = CourseWithLessons("C", 1);
        var lesson = course.Modules.First().Lessons.First();
        var squat = NewExercise("squat", "Squat");
        var hidden = NewExercise("hidden", "Hidden");
        hidden.IsPublished = false;
        db.Exercises.AddRange(squat, hidden);
        lesson.Exercises.Add(new LessonExercise { Id = Guid.NewGuid(), ExerciseId = hidden.Id, SortOrder = 1 });
        lesson.Exercises.Add(new LessonExercise { Id = Guid.NewGuid(), ExerciseId = squat.Id, SortOrder = 2 });
        db.Courses.Add(course);
        db.Enrollments.Add(new Enrollment { Id = Guid.NewGuid(), UserId = UserId, CourseId = course.Id });
        await db.SaveChangesAsync();
        var (svc, _, _) = NewService(db);

        var detail = (await svc.GetLessonDetailAsync(UserId, lesson.Id, "en")).Value!;

        detail.Exercises.ShouldHaveSingleItem().Slug.ShouldBe("squat");
    }

    [Fact]
    public async Task GetLessonDetail_LessonWithoutExercises_ReturnsEmptyList()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var course = CourseWithLessons("C", 1);
        db.Courses.Add(course);
        db.Enrollments.Add(new Enrollment { Id = Guid.NewGuid(), UserId = UserId, CourseId = course.Id });
        await db.SaveChangesAsync();
        var (svc, _, _) = NewService(db);

        var detail = (await svc.GetLessonDetailAsync(UserId, course.Modules.First().Lessons.First().Id, "en")).Value!;

        detail.Exercises.ShouldNotBeNull();
        detail.Exercises.ShouldBeEmpty();
    }

    // ---- CompleteLessonAsync -------------------------------------------------

    [Fact]
    public async Task CompleteLesson_NotEnrolled_ReturnsBadRequest()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var course = CourseWithLessons("C", 1);
        db.Courses.Add(course);
        await db.SaveChangesAsync();
        var lessonId = course.Modules.First().Lessons.First().Id;
        var (svc, _, _) = NewService(db);

        (await svc.CompleteLessonAsync(UserId, lessonId)).StatusCode.ShouldBe(400);
    }

    [Fact]
    public async Task CompleteLesson_FirstCompletion_PersistsAndIncrements()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var course = CourseWithLessons("C", 2);
        db.Courses.Add(course);
        db.Enrollments.Add(new Enrollment { Id = Guid.NewGuid(), UserId = UserId, CourseId = course.Id });
        await db.SaveChangesAsync();
        var lessonId = course.Modules.First().Lessons.First().Id;
        var (svc, _, _) = NewService(db);

        var result = await svc.CompleteLessonAsync(UserId, lessonId);

        result.Value!.CompletedLessons.ShouldBe(1);
        result.Value.CourseCompleted.ShouldBeFalse();
        (await db.LessonCompletions.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task CompleteLesson_Idempotent_DoesNotDoubleCount()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var course = CourseWithLessons("C", 2);
        db.Courses.Add(course);
        db.Enrollments.Add(new Enrollment { Id = Guid.NewGuid(), UserId = UserId, CourseId = course.Id });
        await db.SaveChangesAsync();
        var lessonId = course.Modules.First().Lessons.First().Id;
        var (svc, _, _) = NewService(db);

        await svc.CompleteLessonAsync(UserId, lessonId);
        var second = await svc.CompleteLessonAsync(UserId, lessonId);

        second.Value!.CompletedLessons.ShouldBe(1);
        (await db.LessonCompletions.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task CompleteLesson_FinalLesson_CompletesCourseAndIssuesCertificate()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var course = CourseWithLessons("C", 1);
        db.Courses.Add(course);
        db.Enrollments.Add(new Enrollment { Id = Guid.NewGuid(), UserId = UserId, CourseId = course.Id });
        await db.SaveChangesAsync();
        var lessonId = course.Modules.First().Lessons.First().Id;
        var (svc, _, certs) = NewService(db, Status(certificateAccess: true));

        var result = await svc.CompleteLessonAsync(UserId, lessonId);

        result.Value!.CourseCompleted.ShouldBeTrue();
        result.Value.CertificatePending.ShouldBeFalse();
        (await db.Enrollments.SingleAsync()).Status.ShouldBe(EnrollmentStatus.Completed);
        await certs.Received(1).GetOrCreateAsync(UserId, course.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteLesson_CertificateFailure_IsSwallowedButFlaggedPending()
    {
        // REL-5: completion must still succeed (the failure is swallowed), but the caller now
        // learns generation failed via CertificatePending instead of silence.
        await using var db = DbContextFactory.CreateInMemory();
        var course = CourseWithLessons("C", 1);
        db.Courses.Add(course);
        db.Enrollments.Add(new Enrollment { Id = Guid.NewGuid(), UserId = UserId, CourseId = course.Id });
        await db.SaveChangesAsync();
        var lessonId = course.Modules.First().Lessons.First().Id;
        var (svc, _, certs) = NewService(db, Status(certificateAccess: true));
        certs.GetOrCreateAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<Certificate>(new InvalidOperationException("boom")));

        var result = await svc.CompleteLessonAsync(UserId, lessonId);

        result.IsSuccess.ShouldBeTrue();
        result.Value!.CourseCompleted.ShouldBeTrue();
        result.Value.CertificatePending.ShouldBeTrue();
    }
}

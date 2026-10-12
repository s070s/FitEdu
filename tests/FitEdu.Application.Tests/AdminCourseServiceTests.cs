using Ganss.Xss;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using FitEdu.Application.DTOs;
using FitEdu.TestSupport;
using FitEdu.Application.ApiServices;
using FitEdu.Application.Mappings;
using FitEdu.Infrastructure.Data;
using FitEdu.Domain.Entities;
using FitEdu.Domain.Enums;
using Shouldly;
using Xunit;

namespace FitEdu.Application.Tests;

public class AdminCourseServiceTests
{
    private const string Admin = "admin-1";

    private static AdminCourseService NewService(ApplicationDbContext db) =>
        new(db, NullLogger<AdminCourseService>.Instance, new HtmlSanitizer());

    private static SaveCourseRequest Request(
        string titleEn = "Title", string? descEn = null, SubscriptionTier tier = SubscriptionTier.Free,
        Guid? categoryId = null, string? newCategoryName = null, string? coverExerciseSlug = null) =>
        new(titleEn, null, descEn, null, tier, categoryId, newCategoryName,
            CoverExerciseSlug: coverExerciseSlug);

    private static SaveLessonRequest LessonRequest(
        Guid moduleId, List<LessonExerciseEditDto>? exercises = null, string titleEn = "Lesson") =>
        new(titleEn, null, null, null, null, null, 1, moduleId, exercises);

    private static Exercise NewExercise(string slug) => new()
    {
        Id = Guid.NewGuid(),
        Slug = slug,
        NameEn = $"{slug} name",
        Category = "strength",
        Level = "beginner",
        PrimaryMuscles = "chest",
        SecondaryMuscles = string.Empty,
        InstructionsEn = "Step one.",
        IsPublished = true
    };

    private static Lesson NewLesson() => new() { Id = Guid.NewGuid(), TitleEn = "Old lesson", ModuleId = Guid.NewGuid() };

    private static LessonExercise Prescription(Exercise exercise, int sortOrder, int? sets = null) => new()
    {
        Id = Guid.NewGuid(),
        ExerciseId = exercise.Id,
        SortOrder = sortOrder,
        Sets = sets
    };

    private static Task<List<LessonExercise>> RowsOfLessonAsync(ApplicationDbContext db, Guid lessonId) =>
        db.LessonExercises
            .AsNoTracking()
            .Where(row => row.LessonId == lessonId)
            .OrderBy(row => row.SortOrder)
            .ToListAsync();

    [Fact]
    public async Task GetCourseById_Missing_ReturnsNull()
    {
        await using var db = DbContextFactory.CreateInMemory();

        (await NewService(db).GetCourseByIdAsync(Guid.NewGuid())).ShouldBeNull();
    }

    [Fact]
    public async Task GetCourseById_ReturnsCountsForModulesLessonsEnrollments()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var course = new Course { Id = Guid.NewGuid(), TitleEn = "C" };
        var module = new Module { Id = Guid.NewGuid(), TitleEn = "M", CourseId = course.Id };
        module.Lessons.Add(new Lesson { Id = Guid.NewGuid(), TitleEn = "L1" });
        module.Lessons.Add(new Lesson { Id = Guid.NewGuid(), TitleEn = "L2" });
        course.Modules.Add(module);
        db.Courses.Add(course);
        db.Enrollments.Add(new Enrollment { Id = Guid.NewGuid(), UserId = "u", CourseId = course.Id });
        await db.SaveChangesAsync();

        var dto = await NewService(db).GetCourseByIdAsync(course.Id);

        dto!.ModuleCount.ShouldBe(1);
        dto.TotalLessons.ShouldBe(2);
        dto.EnrollmentCount.ShouldBe(1);
    }

    [Fact]
    public async Task GetCourses_OrdersByCreatedAtDescending()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var older = new Course { Id = Guid.NewGuid(), TitleEn = "Older" };
        var newer = new Course { Id = Guid.NewGuid(), TitleEn = "Newer" };
        db.Courses.AddRange(older, newer);
        await db.SaveChangesAsync();
        // Audit stamping overwrites CreatedAt on insert; reassign on a 2nd (Modified) save to get distinct values.
        older.CreatedAt = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        newer.CreatedAt = new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await db.SaveChangesAsync();

        var page = await NewService(db).GetCoursesAsync(1, 10, "createdat", "desc");

        page.Items.Select(i => i.TitleEn).ShouldBe(new[] { "Newer", "Older" });
    }

    [Fact]
    public async Task GetCourses_SearchFiltersByTitle_OnSqlite()
    {
        // UX-13: EF.Functions.Like is unsupported on the InMemory provider — use the relational SQLite fixture.
        await using var db = DbContextFactory.CreateSqlite();
        db.Courses.Add(new Course { Id = Guid.NewGuid(), TitleEn = "Alpha Course" });
        db.Courses.Add(new Course { Id = Guid.NewGuid(), TitleEn = "Beta Course" });
        await db.SaveChangesAsync();

        var page = await NewService(db).GetCoursesAsync(1, 10, "createdat", "desc", search: "alpha");

        page.Items.Select(i => i.TitleEn).ShouldBe(new[] { "Alpha Course" });
        page.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task CreateCourse_PersistsUnpublishedWithZeroCounts()
    {
        await using var db = DbContextFactory.CreateInMemory();

        var dto = (await NewService(db).CreateCourseAsync(Request("New Course"), Admin)).Value!;

        dto.IsPublished.ShouldBeFalse();
        dto.ModuleCount.ShouldBe(0);
        dto.EnrollmentCount.ShouldBe(0);
        (await db.Courses.FindAsync(dto.Id)).ShouldNotBeNull();
    }

    [Fact]
    public async Task CreateCourse_SanitizesDescriptionHtml()
    {
        await using var db = DbContextFactory.CreateInMemory();

        var dto = (await NewService(db)
            .CreateCourseAsync(Request(descEn: "<script>alert(1)</script><p>Hello</p>"), Admin)).Value!;

        dto.DescriptionEn!.ShouldNotContain("<script");
        dto.DescriptionEn!.ShouldContain("Hello");
    }

    [Fact]
    public async Task CreateCourse_NullDescription_StaysNull()
    {
        await using var db = DbContextFactory.CreateInMemory();

        var dto = (await NewService(db).CreateCourseAsync(Request(descEn: null), Admin)).Value!;

        dto.DescriptionEn.ShouldBeNull();
    }

    [Fact]
    public async Task UpdateCourse_Missing_ReturnsNull()
    {
        await using var db = DbContextFactory.CreateInMemory();

        (await NewService(db).UpdateCourseAsync(Guid.NewGuid(), Request(), Admin)).StatusCode.ShouldBe(404);
    }

    [Fact]
    public async Task UpdateCourse_UpdatesFieldsAndSanitizes()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var course = new Course { Id = Guid.NewGuid(), TitleEn = "Old" };
        db.Courses.Add(course);
        await db.SaveChangesAsync();

        var dto = (await NewService(db).UpdateCourseAsync(
            course.Id, Request("Updated", descEn: "<b onclick=\"x\">bold</b>", tier: SubscriptionTier.Pro), Admin)).Value;

        dto!.TitleEn.ShouldBe("Updated");
        dto.RequiredTier.ShouldBe(SubscriptionTier.Pro);
        dto.DescriptionEn!.ShouldNotContain("onclick");
    }

    [Fact]
    public async Task DeleteCourse_Missing_ReturnsFalse()
    {
        await using var db = DbContextFactory.CreateInMemory();

        (await NewService(db).DeleteCourseAsync(Guid.NewGuid(), Admin)).ShouldBeFalse();
    }

    [Fact]
    public async Task DeleteCourse_SoftDeletes_HiddenFromSubsequentQueries()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var course = new Course { Id = Guid.NewGuid(), TitleEn = "C" };
        db.Courses.Add(course);
        await db.SaveChangesAsync();
        var svc = NewService(db);

        (await svc.DeleteCourseAsync(course.Id, Admin)).ShouldBeTrue();

        (await svc.GetCourseByIdAsync(course.Id)).ShouldBeNull();
    }

    [Fact]
    public async Task DeleteCourse_CascadesSoftDeleteToModulesAndLessons()
    {
        // DB-4: a soft-deleted course's modules/lessons used to stay IsDeleted=false — still
        // live, still directly readable/editable via the admin module/lesson endpoints even
        // though the parent course was gone from every course-scoped view.
        await using var db = DbContextFactory.CreateInMemory();
        var course = new Course { Id = Guid.NewGuid(), TitleEn = "C" };
        var module = new Module { Id = Guid.NewGuid(), TitleEn = "M", CourseId = course.Id };
        var lesson1 = new Lesson { Id = Guid.NewGuid(), TitleEn = "L1" };
        var lesson2 = new Lesson { Id = Guid.NewGuid(), TitleEn = "L2" };
        module.Lessons.Add(lesson1);
        module.Lessons.Add(lesson2);
        course.Modules.Add(module);
        db.Courses.Add(course);
        await db.SaveChangesAsync();

        (await NewService(db).DeleteCourseAsync(course.Id, Admin)).ShouldBeTrue();

        var reloadedModule = await db.Modules.IgnoreQueryFilters().SingleAsync(m => m.Id == module.Id);
        reloadedModule.IsDeleted.ShouldBeTrue();
        reloadedModule.DeletedAt.ShouldNotBeNull();
        (await db.Lessons.IgnoreQueryFilters().Where(l => l.IsDeleted).CountAsync()).ShouldBe(2);
        (await db.Modules.CountAsync()).ShouldBe(0); // hidden by the soft-delete filter now
        (await db.Lessons.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task PublishCourse_SetsPublished()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var course = new Course { Id = Guid.NewGuid(), TitleEn = "C", IsPublished = false };
        db.Courses.Add(course);
        await db.SaveChangesAsync();
        var svc = NewService(db);

        (await svc.PublishCourseAsync(course.Id, Admin)).ShouldBeTrue();
        (await svc.GetCourseByIdAsync(course.Id))!.IsPublished.ShouldBeTrue();
    }

    [Fact]
    public async Task UnpublishCourse_ClearsPublished()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var course = new Course { Id = Guid.NewGuid(), TitleEn = "C", IsPublished = true };
        db.Courses.Add(course);
        await db.SaveChangesAsync();
        var svc = NewService(db);

        (await svc.UnpublishCourseAsync(course.Id, Admin)).ShouldBeTrue();
        (await svc.GetCourseByIdAsync(course.Id))!.IsPublished.ShouldBeFalse();
    }

    [Fact]
    public async Task PublishCourse_Missing_ReturnsFalse()
    {
        await using var db = DbContextFactory.CreateInMemory();

        (await NewService(db).PublishCourseAsync(Guid.NewGuid(), Admin)).ShouldBeFalse();
    }

    [Fact]
    public async Task CreateCourse_WithExistingCategoryId_AssignsCategory()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var category = new Category { Id = Guid.NewGuid(), NameEn = "Career" };
        db.Categories.Add(category);
        await db.SaveChangesAsync();

        var dto = (await NewService(db).CreateCourseAsync(Request(categoryId: category.Id), Admin)).Value!;

        dto.CategoryId.ShouldBe(category.Id);
        dto.CategoryNameEn.ShouldBe("Career");
    }

    [Fact]
    public async Task CreateCourse_WithNewCategoryName_CreatesAndAssignsCategory()
    {
        await using var db = DbContextFactory.CreateInMemory();

        var dto = (await NewService(db).CreateCourseAsync(Request(newCategoryName: "Finance"), Admin)).Value!;

        dto.CategoryId.ShouldNotBeNull();
        dto.CategoryNameEn.ShouldBe("Finance");
        (await db.Categories.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task CreateCourse_NewCategoryName_ReusesExistingByCaseInsensitiveMatch()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var existing = new Category { Id = Guid.NewGuid(), NameEn = "Finance" };
        db.Categories.Add(existing);
        await db.SaveChangesAsync();

        var dto = (await NewService(db).CreateCourseAsync(Request(newCategoryName: "finance"), Admin)).Value!;

        dto.CategoryId.ShouldBe(existing.Id);
        (await db.Categories.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task CreateCourse_NoCategory_LeavesCategoryNull()
    {
        await using var db = DbContextFactory.CreateInMemory();

        var dto = (await NewService(db).CreateCourseAsync(Request(), Admin)).Value!;

        dto.CategoryId.ShouldBeNull();
        dto.CategoryNameEn.ShouldBeNull();
    }

    [Fact]
    public async Task UpdateCourse_ChangesCategory()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var oldCategory = new Category { Id = Guid.NewGuid(), NameEn = "Old" };
        var newCategory = new Category { Id = Guid.NewGuid(), NameEn = "New" };
        db.Categories.AddRange(oldCategory, newCategory);
        var course = new Course { Id = Guid.NewGuid(), TitleEn = "C", CategoryId = oldCategory.Id };
        db.Courses.Add(course);
        await db.SaveChangesAsync();

        var dto = (await NewService(db).UpdateCourseAsync(course.Id, Request(categoryId: newCategory.Id), Admin)).Value;

        dto!.CategoryId.ShouldBe(newCategory.Id);
    }

    // ── Fitness fields ──

    [Fact]
    public async Task CreateCourse_SavesFitnessFields_AndNormalisesEquipmentCodes()
    {
        await using var db = DbContextFactory.CreateInMemory();
        db.Exercises.Add(NewExercise("push-ups"));
        await db.SaveChangesAsync();
        var request = Request() with
        {
            Level = CourseLevel.Intermediate,
            Goal = FitnessGoal.FatLoss,
            Equipment = " Dumbbells , mat,,dumbbells ",
            EstimatedWeeks = 8,
            SessionsPerWeek = 3,
            CoverExerciseSlug = " Push-Ups "
        };

        var result = await NewService(db).CreateCourseAsync(request, Admin);

        result.StatusCode.ShouldBe(201);
        var dto = result.Value!;
        dto.Level.ShouldBe(CourseLevel.Intermediate);
        dto.Goal.ShouldBe(FitnessGoal.FatLoss);
        dto.Equipment.ShouldBe("dumbbells,mat");
        dto.EstimatedWeeks.ShouldBe(8);
        dto.SessionsPerWeek.ShouldBe(3);
        dto.CoverExerciseSlug.ShouldBe("push-ups");
    }

    [Fact]
    public async Task CreateCourse_UnknownCoverExerciseSlug_ReturnsValidationError_AndSavesNothing()
    {
        await using var db = DbContextFactory.CreateInMemory();

        var result = await NewService(db).CreateCourseAsync(Request(coverExerciseSlug: "no-such-move"), Admin);

        result.StatusCode.ShouldBe(400);
        result.ErrorMessage!.ShouldContain("no-such-move");
        (await db.Courses.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task UpdateCourse_UnknownCoverExerciseSlug_ReturnsValidationError_AndKeepsCourse()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var course = new Course { Id = Guid.NewGuid(), TitleEn = "Original" };
        db.Courses.Add(course);
        await db.SaveChangesAsync();

        var result = await NewService(db).UpdateCourseAsync(
            course.Id, Request("Renamed", coverExerciseSlug: "no-such-move"), Admin);

        result.StatusCode.ShouldBe(400);
        var stored = await db.Courses.AsNoTracking().SingleAsync(c => c.Id == course.Id);
        stored.TitleEn.ShouldBe("Original");
        stored.CoverExerciseSlug.ShouldBeNull();
    }

    [Fact]
    public async Task UpdateCourse_BlankFitnessTexts_ClearCoverAndEquipment()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var course = new Course { Id = Guid.NewGuid(), TitleEn = "C", Equipment = "mat", CoverExerciseSlug = "plank" };
        db.Courses.Add(course);
        await db.SaveChangesAsync();

        var request = Request() with { Equipment = "  ", CoverExerciseSlug = " " };
        var dto = (await NewService(db).UpdateCourseAsync(course.Id, request, Admin)).Value!;

        dto.Equipment.ShouldBeNull();
        dto.CoverExerciseSlug.ShouldBeNull();
    }

    [Fact]
    public async Task GetCourseById_ReturnsFitnessFields()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var course = new Course
        {
            Id = Guid.NewGuid(),
            TitleEn = "C",
            Level = CourseLevel.Advanced,
            Goal = FitnessGoal.Strength,
            Equipment = "barbell",
            EstimatedWeeks = 12,
            SessionsPerWeek = 4,
            CoverExerciseSlug = "barbell-squat"
        };
        db.Courses.Add(course);
        await db.SaveChangesAsync();

        var dto = (await NewService(db).GetCourseByIdAsync(course.Id))!;

        dto.Level.ShouldBe(CourseLevel.Advanced);
        dto.Goal.ShouldBe(FitnessGoal.Strength);
        dto.Equipment.ShouldBe("barbell");
        dto.EstimatedWeeks.ShouldBe(12);
        dto.SessionsPerWeek.ShouldBe(4);
        dto.CoverExerciseSlug.ShouldBe("barbell-squat");
    }

    // ── Lesson exercises ──

    [Fact]
    public async Task CreateLesson_WithExercises_SavesRowsInListOrder()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var plank = NewExercise("plank");
        var pushUps = NewExercise("push-ups");
        db.Exercises.AddRange(plank, pushUps);
        await db.SaveChangesAsync();
        var moduleId = Guid.NewGuid();

        var result = await NewService(db).CreateLessonAsync(LessonRequest(moduleId,
        [
            new LessonExerciseEditDto("push-ups", Sets: 3, Reps: "10", RestSeconds: 60),
            new LessonExerciseEditDto("plank", Reps: "30 s")
        ]), Admin);

        result.StatusCode.ShouldBe(201);
        var lessonId = result.Value!.Id;
        var rows = await RowsOfLessonAsync(db, lessonId);
        rows.Select(row => row.ExerciseId).ShouldBe([pushUps.Id, plank.Id]);
        rows.Select(row => row.SortOrder).ShouldBe([1, 2]);
        result.Value.ModuleId.ShouldBe(moduleId);
        result.Value.IsPublished.ShouldBeFalse();
    }

    [Fact]
    public async Task UpdateLesson_WithExercises_ReplacesPreviousRowsWholesale()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var squat = NewExercise("barbell-squat");
        var plank = NewExercise("plank");
        var pushUps = NewExercise("push-ups");
        db.Exercises.AddRange(squat, plank, pushUps);
        var lesson = NewLesson();
        lesson.Exercises.Add(Prescription(squat, sortOrder: 1, sets: 5));
        lesson.Exercises.Add(Prescription(plank, sortOrder: 2, sets: 4));
        db.Lessons.Add(lesson);
        await db.SaveChangesAsync();

        var result = await NewService(db).UpdateLessonAsync(lesson.Id, LessonRequest(lesson.ModuleId,
        [
            new LessonExerciseEditDto(" Push-Ups ", Sets: 3, Reps: "8–12", RestSeconds: 90,
                NotesEn: "Elbows at 45°", NotesEl: "Αγκώνες στις 45°"),
            new LessonExerciseEditDto("plank", Reps: "  ", NotesEn: " ")
        ], titleEn: "New lesson"), Admin);

        result.StatusCode.ShouldBe(200);
        var rows = await RowsOfLessonAsync(db, lesson.Id);
        rows.Count.ShouldBe(2);
        rows.Select(row => row.ExerciseId).ShouldBe([pushUps.Id, plank.Id]);
        rows.Select(row => row.SortOrder).ShouldBe([1, 2]);
        rows[0].Sets.ShouldBe(3);
        rows[0].Reps.ShouldBe("8–12");
        rows[0].RestSeconds.ShouldBe(90);
        rows[0].NotesEn.ShouldBe("Elbows at 45°");
        rows[0].NotesEl.ShouldBe("Αγκώνες στις 45°");
        // The old plank row is gone, not merged: its 4 sets do not carry over, blanks become null.
        rows[1].Sets.ShouldBeNull();
        rows[1].Reps.ShouldBeNull();
        rows[1].NotesEn.ShouldBeNull();

        var dto = result.Value!;
        dto.TitleEn.ShouldBe("New lesson");
        dto.Exercises!.Select(row => row.Slug).ShouldBe(["push-ups", "plank"]);
        dto.Exercises![0].Name.ShouldBe("push-ups name");
    }

    [Fact]
    public async Task UpdateLesson_ReplacesRows_OnSqlite_AndAdminProjectionListsThemInOrder()
    {
        // Relational check: real foreign keys, and the nested exercise list in LessonAdminProjection
        // must translate to SQL (the InMemory provider would accept anything).
        await using var db = DbContextFactory.CreateSqlite();
        var course = new Course { Id = Guid.NewGuid(), TitleEn = "C" };
        var module = new Module { Id = Guid.NewGuid(), TitleEn = "M", CourseId = course.Id };
        var squat = NewExercise("barbell-squat");
        var plank = NewExercise("plank");
        var lesson = new Lesson { Id = Guid.NewGuid(), TitleEn = "L", ModuleId = module.Id };
        lesson.Exercises.Add(Prescription(squat, sortOrder: 1, sets: 5));
        db.Courses.Add(course);
        db.Modules.Add(module);
        db.Exercises.AddRange(squat, plank);
        db.Lessons.Add(lesson);
        await db.SaveChangesAsync();

        var result = await NewService(db).UpdateLessonAsync(lesson.Id, LessonRequest(module.Id,
        [
            new LessonExerciseEditDto("plank", Sets: 2),
            new LessonExerciseEditDto("barbell-squat")
        ]), Admin);

        result.StatusCode.ShouldBe(200);
        db.ChangeTracker.Clear();
        var projected = await db.Lessons
            .Where(l => l.Id == lesson.Id)
            .Select(CourseContentMappings.LessonAdminProjection)
            .SingleAsync();
        projected.Exercises!.Select(row => row.Slug).ShouldBe(["plank", "barbell-squat"]);
        projected.Exercises![0].Sets.ShouldBe(2);
        projected.Exercises![1].Sets.ShouldBeNull();
        (await db.LessonExercises.CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task UpdateLesson_WithoutExerciseList_KeepsExistingRows()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var squat = NewExercise("barbell-squat");
        db.Exercises.Add(squat);
        var lesson = NewLesson();
        lesson.Exercises.Add(Prescription(squat, sortOrder: 1, sets: 5));
        db.Lessons.Add(lesson);
        await db.SaveChangesAsync();

        var result = await NewService(db).UpdateLessonAsync(lesson.Id, LessonRequest(lesson.ModuleId, exercises: null), Admin);

        result.StatusCode.ShouldBe(200);
        (await RowsOfLessonAsync(db, lesson.Id)).Single().Sets.ShouldBe(5);
        result.Value!.Exercises!.Single().Slug.ShouldBe("barbell-squat");
    }

    [Fact]
    public async Task UpdateLesson_EmptyExerciseList_RemovesAllRows()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var squat = NewExercise("barbell-squat");
        db.Exercises.Add(squat);
        var lesson = NewLesson();
        lesson.Exercises.Add(Prescription(squat, sortOrder: 1));
        db.Lessons.Add(lesson);
        await db.SaveChangesAsync();

        var result = await NewService(db).UpdateLessonAsync(lesson.Id, LessonRequest(lesson.ModuleId, exercises: []), Admin);

        result.StatusCode.ShouldBe(200);
        (await RowsOfLessonAsync(db, lesson.Id)).ShouldBeEmpty();
        result.Value!.Exercises!.ShouldBeEmpty();
    }

    [Fact]
    public async Task UpdateLesson_UnknownExerciseSlug_ReturnsValidationError_AndChangesNothing()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var squat = NewExercise("barbell-squat");
        db.Exercises.Add(squat);
        var lesson = NewLesson();
        lesson.Exercises.Add(Prescription(squat, sortOrder: 1, sets: 5));
        db.Lessons.Add(lesson);
        await db.SaveChangesAsync();

        var result = await NewService(db).UpdateLessonAsync(lesson.Id, LessonRequest(lesson.ModuleId,
        [
            new LessonExerciseEditDto("barbell-squat"),
            new LessonExerciseEditDto("no-such-move")
        ], titleEn: "Renamed"), Admin);

        result.StatusCode.ShouldBe(400);
        result.ErrorMessage!.ShouldContain("no-such-move");
        (await RowsOfLessonAsync(db, lesson.Id)).Single().Sets.ShouldBe(5);
        (await db.Lessons.AsNoTracking().SingleAsync(l => l.Id == lesson.Id)).TitleEn.ShouldBe("Old lesson");
    }

    [Fact]
    public async Task CreateLesson_UnknownExerciseSlug_ReturnsValidationError_AndSavesNothing()
    {
        await using var db = DbContextFactory.CreateInMemory();

        var result = await NewService(db).CreateLessonAsync(
            LessonRequest(Guid.NewGuid(), [new LessonExerciseEditDto("no-such-move")]), Admin);

        result.StatusCode.ShouldBe(400);
        (await db.Lessons.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task UpdateLesson_Missing_ReturnsNotFound()
    {
        await using var db = DbContextFactory.CreateInMemory();

        var result = await NewService(db).UpdateLessonAsync(Guid.NewGuid(), LessonRequest(Guid.NewGuid()), Admin);

        result.StatusCode.ShouldBe(404);
    }

    [Fact]
    public async Task UpdateLesson_SanitizesContentAndKeepsVideoWhenNoneSent()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var lesson = NewLesson();
        lesson.VideoPath = "lessons/video/intro.mp4";
        db.Lessons.Add(lesson);
        await db.SaveChangesAsync();
        var request = LessonRequest(lesson.ModuleId) with { ContentEn = "<script>x</script><p>Hi</p>" };

        var dto = (await NewService(db).UpdateLessonAsync(lesson.Id, request, Admin)).Value!;

        dto.ContentEn!.ShouldNotContain("<script");
        dto.VideoPath.ShouldBe("lessons/video/intro.mp4");
    }
}

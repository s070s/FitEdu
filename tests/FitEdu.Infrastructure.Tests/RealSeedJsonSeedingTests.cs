using Microsoft.EntityFrameworkCore;
using FitEdu.Application.DTOs;
using FitEdu.Infrastructure.Data;
using FitEdu.Infrastructure.Seeding;
using FitEdu.TestSupport;
using Shouldly;
using Xunit;

namespace FitEdu.Infrastructure.Tests;

/// <summary>
/// Runs the real seeders, in the Development start-up order, on SQLite against the real repo
/// JSON folders, and checks that everything the files describe arrives in the database.
/// </summary>
[Trait("Category", "SeedContent")]
public sealed class RealSeedJsonSeedingTests : IClassFixture<RealSeedJsonDatabase>
{
    // exercises.json rows with photos (see src/FitEdu.Shared/JSON/Exercises/README.md).
    private const int ExpectedExerciseCount = 873;
    private const int ExpectedCourseCount = 19;
    private const int ExpectedAssessmentCount = 19;

    private readonly RealSeedJsonDatabase _seeded;

    public RealSeedJsonSeedingTests(RealSeedJsonDatabase seeded)
    {
        _seeded = seeded;
    }

    private ApplicationDbContext Db => _seeded.Db;

    [Fact]
    public void Seeding_ReportsNoWarningsOrErrors()
    {
        _seeded.Logger.Warnings.ShouldBeEmpty();
        _seeded.Logger.Errors.ShouldBeEmpty();
    }

    [Fact]
    public async Task Exercises_EveryLibraryRowIsSeeded()
    {
        (await Db.Exercises.CountAsync()).ShouldBe(ExpectedExerciseCount);
    }

    [Fact]
    public async Task Courses_EveryCourseModuleAndLessonInTheFilesIsSeeded()
    {
        var courses = SeedJsonFiles.LoadCourses().Select(file => file.Content).ToList();
        var moduleCount = courses.Sum(course => course.Modules.Count);
        var lessonCount = courses.Sum(course => course.Modules.Sum(module => module.Lessons.Count));

        (await Db.Courses.CountAsync()).ShouldBe(ExpectedCourseCount);
        (await Db.Modules.CountAsync()).ShouldBe(moduleCount);
        (await Db.Lessons.CountAsync()).ShouldBe(lessonCount);
    }

    [Fact]
    public async Task LessonExercises_EveryPrescriptionIsLinkedToItsLibraryExerciseInListOrder()
    {
        var expectedRows = SeedJsonFiles.LoadCourses()
            .SelectMany(file => DescribePrescriptions(file.Content))
            .Order(StringComparer.Ordinal)
            .ToList();

        var seededPrescriptions = await Db.LessonExercises
            .Select(prescription => new
            {
                Course = prescription.Lesson.Module.Course.TitleEn,
                ModuleSortOrder = prescription.Lesson.Module.SortOrder,
                LessonSortOrder = prescription.Lesson.SortOrder,
                prescription.SortOrder,
                prescription.Exercise.Slug
            })
            .ToListAsync();
        var seededRows = seededPrescriptions
            .Select(row => DescribePrescription(row.Course, row.ModuleSortOrder, row.LessonSortOrder, row.SortOrder, row.Slug))
            .Order(StringComparer.Ordinal)
            .ToList();

        seededRows.Count.ShouldBe(expectedRows.Count);
        seededRows.ShouldBe(expectedRows);
    }

    [Fact]
    public async Task Assessments_EveryFileIsSeededUnderItsKey()
    {
        var fileKeys = SeedJsonFiles.LoadAssessments().Select(file => file.Content.Key).Order(StringComparer.Ordinal).ToList();

        var seededKeys = await Db.AssessmentDefinitions.Select(definition => definition.Key).ToListAsync();

        seededKeys.Count.ShouldBe(ExpectedAssessmentCount);
        seededKeys.Order(StringComparer.Ordinal).ShouldBe(fileKeys);
    }

    [Fact]
    public async Task Categories_AllHaveTheGreekNameEveryCourseAndAssessmentFileGives()
    {
        // Courses and assessments share the Category table, so a name used by both
        // (e.g. Mobility) must carry the same Greek name in every file.
        var namesInFiles = SeedJsonFiles.LoadCourses().Select(file => (file.Content.Category, file.Content.CategoryEl))
            .Concat(SeedJsonFiles.LoadAssessments().Select(file => (file.Content.Category, file.Content.CategoryEl)))
            .Distinct()
            .ToList();

        var greekNamesByEnglishName = await Db.Categories.ToDictionaryAsync(category => category.NameEn, category => category.NameEl);

        greekNamesByEnglishName.Values.ShouldAllBe(greekName => !string.IsNullOrWhiteSpace(greekName));
        foreach (var (englishName, greekName) in namesInFiles)
        {
            greekNamesByEnglishName.ShouldContainKey(englishName!);
            greekNamesByEnglishName[englishName!].ShouldBe(greekName, $"Greek name of category {englishName}");
        }
    }

    [Fact]
    public async Task ReferencedExercises_HaveGreekNames()
    {
        var coverSlugs = await Db.Courses
            .Where(course => course.CoverExerciseSlug != null)
            .Select(course => course.CoverExerciseSlug!)
            .ToListAsync();

        var untranslatedSlugs = await Db.Exercises
            .Where(exercise => coverSlugs.Contains(exercise.Slug) || Db.LessonExercises.Any(prescription => prescription.ExerciseId == exercise.Id))
            .Where(exercise => exercise.NameEl == null || exercise.NameEl == string.Empty)
            .Select(exercise => exercise.Slug)
            .ToListAsync();

        untranslatedSlugs.ShouldBeEmpty();
    }

    [Fact]
    public async Task SeededText_FitsTheColumnLengthLimits()
    {
        // SQLite ignores HasMaxLength, so a too-long seed text would only fail on SQL Server.
        // Compare every seeded string with the limit the EF model gives its column.
        var seededRows = new List<object>();
        seededRows.AddRange(await Db.Exercises.AsNoTracking().ToListAsync());
        seededRows.AddRange(await Db.Categories.AsNoTracking().ToListAsync());
        seededRows.AddRange(await Db.Courses.AsNoTracking().ToListAsync());
        seededRows.AddRange(await Db.Modules.AsNoTracking().ToListAsync());
        seededRows.AddRange(await Db.Lessons.AsNoTracking().ToListAsync());
        seededRows.AddRange(await Db.LessonExercises.AsNoTracking().ToListAsync());
        seededRows.AddRange(await Db.AssessmentDefinitions.AsNoTracking().ToListAsync());

        var problems = seededRows.SelectMany(FindValuesOverTheirLimit).ToList();

        seededRows.ShouldNotBeEmpty();
        problems.ShouldBeEmpty();
    }

    /// <summary>Every string property of the row that is longer than its column's max length.</summary>
    private IEnumerable<string> FindValuesOverTheirLimit(object row)
    {
        // Entry() on an untracked row reads its values without starting to track it.
        foreach (var property in Db.Entry(row).Properties)
        {
            var maxLength = property.Metadata.GetMaxLength();
            if (maxLength is not null && property.CurrentValue is string value && value.Length > maxLength)
                yield return $"{row.GetType().Name}.{property.Metadata.Name}: {value.Length} characters, limit {maxLength}";
        }
    }

    private static List<string> DescribePrescriptions(CourseSeedDto course)
    {
        var rows = new List<string>();

        foreach (var module in course.Modules)
            rows.AddRange(module.Lessons.SelectMany(lesson => DescribeLessonPrescriptions(course.Title, module.SortOrder, lesson)));

        return rows;
    }

    // The seeder numbers prescriptions 1, 2, 3… in list order.
    private static IEnumerable<string> DescribeLessonPrescriptions(string course, int moduleSortOrder, LessonSeedDto lesson) =>
        lesson.Exercises.Select((exercise, index) =>
            DescribePrescription(course, moduleSortOrder, lesson.SortOrder, index + 1, exercise.Slug));

    private static string DescribePrescription(string course, int moduleSortOrder, int lessonSortOrder, int sortOrder, string slug) =>
        $"{course} | module {moduleSortOrder} | lesson {lessonSortOrder} | #{sortOrder} {slug}";
}

/// <summary>
/// One SQLite database filled once by the real seeders and shared by the tests in
/// <see cref="RealSeedJsonSeedingTests"/> (873 exercises are too slow to seed per test).
/// </summary>
public sealed class RealSeedJsonDatabase : IAsyncLifetime
{
    public ApplicationDbContext Db { get; } = DbContextFactory.CreateSqlite();

    public RecordingLogger Logger { get; } = new();

    public async Task InitializeAsync()
    {
        await ExerciseSeeder.SeedFromJsonAsync(Db, SeedJsonFiles.ExercisesFolder, Logger);
        await CourseSeeder.SeedFromJsonAsync(Db, SeedJsonFiles.CoursesFolder, Logger);
        await AssessmentSeeder.SeedFromJsonAsync(Db, SeedJsonFiles.AssessmentsFolder, Logger);

        // The tests read what was saved, not the entities the seeders still track.
        Db.ChangeTracker.Clear();
    }

    public async Task DisposeAsync() => await Db.DisposeAsync();
}

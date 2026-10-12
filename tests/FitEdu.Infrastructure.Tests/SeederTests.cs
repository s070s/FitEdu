using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using FitEdu.Application.DTOs;
using FitEdu.TestSupport;
using FitEdu.Infrastructure.Data;
using FitEdu.Infrastructure.Seeding;
using FitEdu.Domain.Entities;
using FitEdu.Domain.Enums;
using FitEdu.Domain.Identity;
using Shouldly;
using Xunit;

namespace FitEdu.Infrastructure.Tests;

public sealed class SeederTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    private string TempDirWith(string fileName, string content) => TempDirWithFiles((fileName, content));

    /// <summary>Creates a temp folder holding each file; a relative path may include a sub-folder.</summary>
    private string TempDirWithFiles(params (string RelativePath, string Content)[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "fitedu-seed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);

        foreach (var (relativePath, content) in files)
        {
            var fullPath = Path.Combine(dir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
        }

        return dir;
    }

    private static string NonExistentDir() => Path.Combine(Path.GetTempPath(), "fitedu-missing-" + Guid.NewGuid().ToString("N"));

    // ---- SubscriptionPlanSeeder ---------------------------------------------

    [Fact]
    public async Task SubscriptionPlanSeeder_SeedsThreeTiers()
    {
        await using var db = DbContextFactory.CreateInMemory();

        await SubscriptionPlanSeeder.SeedAsync(db, NullLogger.Instance);

        var plans = await db.SubscriptionPlans.ToListAsync();
        plans.Count.ShouldBe(3);
        plans.Select(p => p.Tier).ShouldBe(
            new[] { SubscriptionTier.Free, SubscriptionTier.Plus, SubscriptionTier.Pro }, ignoreOrder: true);
    }

    [Fact]
    public async Task SubscriptionPlanSeeder_IsIdempotent()
    {
        await using var db = DbContextFactory.CreateInMemory();

        await SubscriptionPlanSeeder.SeedAsync(db, NullLogger.Instance);
        await SubscriptionPlanSeeder.SeedAsync(db, NullLogger.Instance);

        (await db.SubscriptionPlans.CountAsync()).ShouldBe(3);
    }

    // ---- BlogArticleSeeder ---------------------------------------------------

    [Fact]
    public async Task BlogArticleSeeder_SeedsArticles_AndIsIdempotent()
    {
        await using var db = DbContextFactory.CreateInMemory();

        await BlogArticleSeeder.SeedAsync(db, NullLogger.Instance);
        var firstCount = await db.BlogArticles.CountAsync();

        await BlogArticleSeeder.SeedAsync(db, NullLogger.Instance);
        var secondCount = await db.BlogArticles.CountAsync();

        firstCount.ShouldBeGreaterThan(0);
        secondCount.ShouldBe(firstCount); // articles already present → skipped, no delete/reseed
    }

    [Fact]
    public async Task BlogArticleSeeder_NeverDeletesExistingEnglishOnlyArticles()
    {
        // OPS-1 regression: the seeder used to treat "no article has a Greek title" as
        // pre-bilingual leftover data and delete + reseed it — silently destroying a real
        // operator's English-only articles. It must now leave any existing data alone.
        await using var db = DbContextFactory.CreateInMemory();
        db.BlogArticles.Add(new()
        {
            Id = Guid.NewGuid(),
            TitleEn = "A Real Operator's Article",
            TitleEl = null,
            Slug = "a-real-operators-article",
            SummaryEn = "Written by a human, not the seeder.",
            ContentEn = "Do not delete me.",
            AuthorName = "The Operator"
        });
        await db.SaveChangesAsync();

        await BlogArticleSeeder.SeedAsync(db, NullLogger.Instance);

        (await db.BlogArticles.CountAsync()).ShouldBe(1);
        (await db.BlogArticles.SingleAsync()).TitleEn.ShouldBe("A Real Operator's Article");
    }

    // ---- CourseSeeder --------------------------------------------------------

    private const string SampleCourseJson = """
    {
      "title": "Intro Course",
      "description": "Desc",
      "isPublished": true,
      "modules": [
        { "title": "Module 1", "sortOrder": 1,
          "lessons": [ { "title": "Lesson 1", "sortOrder": 1, "durationMinutes": 10 } ] }
      ]
    }
    """;

    [Fact]
    public async Task CourseSeeder_ValidJson_SeedsCourseWithModulesAndLessons()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var dir = TempDirWith("course.json", SampleCourseJson);

        await CourseSeeder.SeedFromJsonAsync(db, dir, NullLogger.Instance);

        var course = await db.Courses.Include(c => c.Modules).ThenInclude(m => m.Lessons).SingleAsync();
        course.TitleEn.ShouldBe("Intro Course");
        course.Modules.Count.ShouldBe(1);
        course.Modules.Single().Lessons.Count.ShouldBe(1);
    }

    [Fact]
    public async Task CourseSeeder_MissingDirectory_IsNoOp()
    {
        await using var db = DbContextFactory.CreateInMemory();

        await Should.NotThrowAsync(() => CourseSeeder.SeedFromJsonAsync(db, NonExistentDir(), NullLogger.Instance));

        (await db.Courses.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task CourseSeeder_WhenCoursesExist_SkipsSeeding()
    {
        await using var db = DbContextFactory.CreateInMemory();
        db.Courses.Add(new FitEdu.Domain.Entities.Course { Id = Guid.NewGuid(), TitleEn = "Existing" });
        await db.SaveChangesAsync();
        var dir = TempDirWith("course.json", SampleCourseJson);

        await CourseSeeder.SeedFromJsonAsync(db, dir, NullLogger.Instance);

        (await db.Courses.CountAsync()).ShouldBe(1); // unchanged
    }

    private const string FitnessCourseJson = """
    {
      "title": "Bodyweight Basics",
      "level": "intermediate",
      "goal": "FATLOSS",
      "equipment": "none,mat",
      "estimatedWeeks": 6,
      "sessionsPerWeek": 3,
      "coverExerciseSlug": "pushups",
      "modules": [
        { "title": "Week 1", "sortOrder": 1,
          "lessons": [ { "title": "Workout A", "sortOrder": 1,
            "exercises": [
              { "slug": "pushups", "sets": 3, "reps": "10", "restSeconds": 60, "notes": "Elbows 45°", "notesEl": "Αγκώνες 45°" },
              { "slug": "plank", "reps": "30 s" }
            ] } ] }
      ]
    }
    """;

    /// <summary>One course whose only lesson ("Workout A") prescribes the given exercise.</summary>
    private static string CourseJsonWithExercise(string lessonExerciseSlug, string coverExerciseSlug) => $$"""
    {
      "title": "Bodyweight Basics",
      "coverExerciseSlug": "{{coverExerciseSlug}}",
      "modules": [
        { "title": "Week 1", "sortOrder": 1,
          "lessons": [ { "title": "Workout A", "sortOrder": 1,
            "exercises": [ { "slug": "{{lessonExerciseSlug}}", "sets": 3 } ] } ] }
      ]
    }
    """;

    /// <summary>Saves minimal library exercises so course lessons can link them by slug.</summary>
    private static async Task<Dictionary<string, Guid>> AddExercisesAsync(ApplicationDbContext db, params string[] slugs)
    {
        var exercises = slugs.Select(slug => new Exercise
        {
            Id = Guid.NewGuid(),
            Slug = slug,
            NameEn = slug,
            Category = "strength",
            Level = "beginner",
            PrimaryMuscles = "chest",
            SecondaryMuscles = string.Empty,
            InstructionsEn = "Step one."
        }).ToList();

        db.Exercises.AddRange(exercises);
        await db.SaveChangesAsync();
        return exercises.ToDictionary(exercise => exercise.Slug, exercise => exercise.Id);
    }

    [Fact]
    public async Task CourseSeeder_FitnessFields_AreMappedCaseInsensitively()
    {
        await using var db = DbContextFactory.CreateInMemory();
        await AddExercisesAsync(db, "pushups", "plank");
        var dir = TempDirWith("course.json", FitnessCourseJson);

        await CourseSeeder.SeedFromJsonAsync(db, dir, NullLogger.Instance);

        var course = await db.Courses.SingleAsync();
        course.Level.ShouldBe(CourseLevel.Intermediate);
        course.Goal.ShouldBe(FitnessGoal.FatLoss);
        course.Equipment.ShouldBe("none,mat");
        course.EstimatedWeeks.ShouldBe(6);
        course.SessionsPerWeek.ShouldBe(3);
        course.CoverExerciseSlug.ShouldBe("pushups");
    }

    [Fact]
    public async Task CourseSeeder_LessonExercises_AreLinkedBySlugInListOrder()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var exerciseIds = await AddExercisesAsync(db, "pushups", "plank");
        var dir = TempDirWith("course.json", FitnessCourseJson);

        await CourseSeeder.SeedFromJsonAsync(db, dir, NullLogger.Instance);

        var lesson = await db.Lessons.Include(l => l.Exercises).SingleAsync();
        var prescriptions = lesson.Exercises.OrderBy(x => x.SortOrder).ToList();
        prescriptions.Count.ShouldBe(2);

        var pushups = prescriptions[0];
        pushups.ExerciseId.ShouldBe(exerciseIds["pushups"]);
        pushups.SortOrder.ShouldBe(1);
        pushups.Sets.ShouldBe(3);
        pushups.Reps.ShouldBe("10");
        pushups.RestSeconds.ShouldBe(60);
        pushups.NotesEn.ShouldBe("Elbows 45°");
        pushups.NotesEl.ShouldBe("Αγκώνες 45°");

        var plank = prescriptions[1];
        plank.ExerciseId.ShouldBe(exerciseIds["plank"]);
        plank.SortOrder.ShouldBe(2);
        plank.Sets.ShouldBeNull();
        plank.Reps.ShouldBe("30 s");
        plank.RestSeconds.ShouldBeNull();
        plank.NotesEn.ShouldBeNull();
    }

    [Fact]
    public async Task CourseSeeder_UnknownLessonExerciseSlug_ThrowsNamingLessonAndSlug()
    {
        await using var db = DbContextFactory.CreateInMemory();
        await AddExercisesAsync(db, "pushups");
        var dir = TempDirWith("course.json", CourseJsonWithExercise(lessonExerciseSlug: "push-ups", coverExerciseSlug: "pushups"));

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => CourseSeeder.SeedFromJsonAsync(db, dir, NullLogger.Instance));

        exception.Message.ShouldContain("'Workout A'");
        exception.Message.ShouldContain("'push-ups'");
        (await db.Courses.CountAsync()).ShouldBe(0); // nothing half-seeded
    }

    [Fact]
    public async Task CourseSeeder_UnknownCoverExerciseSlug_ThrowsNamingCourseAndSlug()
    {
        await using var db = DbContextFactory.CreateInMemory();
        await AddExercisesAsync(db, "pushups");
        var dir = TempDirWith("course.json", CourseJsonWithExercise(lessonExerciseSlug: "pushups", coverExerciseSlug: "burpee"));

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => CourseSeeder.SeedFromJsonAsync(db, dir, NullLogger.Instance));

        exception.Message.ShouldContain("'Bodyweight Basics'");
        exception.Message.ShouldContain("'burpee'");
        (await db.Courses.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task CourseSeeder_JsonWithoutFitnessFields_UsesDefaults()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var dir = TempDirWith("course.json", SampleCourseJson);

        await CourseSeeder.SeedFromJsonAsync(db, dir, NullLogger.Instance);

        var course = await db.Courses.SingleAsync();
        course.RequiredTier.ShouldBe(SubscriptionTier.Free);
        course.Level.ShouldBe(CourseLevel.Beginner);
        course.Goal.ShouldBe(FitnessGoal.GeneralFitness);
        course.Equipment.ShouldBeNull();
        course.EstimatedWeeks.ShouldBeNull();
        course.SessionsPerWeek.ShouldBeNull();
        course.CoverExerciseSlug.ShouldBeNull();
        course.CategoryId.ShouldBeNull();
        (await db.LessonExercises.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task CourseSeeder_UnknownEnumValues_FallBackToDefaults()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var dir = TempDirWith("course.json", """
        { "title": "Odd Values", "requiredTier": "Gold", "level": "Olympian", "goal": "99" }
        """);

        await CourseSeeder.SeedFromJsonAsync(db, dir, NullLogger.Instance);

        var course = await db.Courses.SingleAsync();
        course.RequiredTier.ShouldBe(SubscriptionTier.Free);
        course.Level.ShouldBe(CourseLevel.Beginner);
        course.Goal.ShouldBe(FitnessGoal.GeneralFitness);
    }

    // ---- CategorySeedHelper (through the course and assessment seeders) -----

    private const string StrengthCourseJson = """
    { "title": "Strong Start", "category": "strength", "categoryEl": "Δύναμη" }
    """;

    [Fact]
    public async Task CategorySeedHelper_SetsGreekName()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var dir = TempDirWith("course.json", StrengthCourseJson);

        await CourseSeeder.SeedFromJsonAsync(db, dir, NullLogger.Instance);

        var category = await db.Categories.SingleAsync();
        category.NameEn.ShouldBe("strength");
        category.NameEl.ShouldBe("Δύναμη");
    }

    [Fact]
    public async Task CategorySeedHelper_BackfillsGreekNameOfExistingCategory()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        await using (var setupDb = DbContextFactory.CreateInMemory(databaseName))
        {
            setupDb.Categories.Add(new Category { Id = Guid.NewGuid(), NameEn = "Strength" });
            await setupDb.SaveChangesAsync();
        }

        var dir = TempDirWith("course.json", StrengthCourseJson);
        await using (var seedDb = DbContextFactory.CreateInMemory(databaseName))
        {
            await CourseSeeder.SeedFromJsonAsync(seedDb, dir, NullLogger.Instance);
        }

        await using var readDb = DbContextFactory.CreateInMemory(databaseName);
        var category = await readDb.Categories.SingleAsync();
        category.NameEn.ShouldBe("Strength");
        category.NameEl.ShouldBe("Δύναμη");
    }

    [Fact]
    public async Task CategorySeedHelper_KeepsExistingGreekName()
    {
        await using var db = DbContextFactory.CreateInMemory();
        db.Categories.Add(new Category { Id = Guid.NewGuid(), NameEn = "Strength", NameEl = "Ισχύς" });
        await db.SaveChangesAsync();
        var dir = TempDirWith("course.json", StrengthCourseJson);

        await CourseSeeder.SeedFromJsonAsync(db, dir, NullLogger.Instance);

        (await db.Categories.SingleAsync()).NameEl.ShouldBe("Ισχύς");
    }

    [Fact]
    public async Task AssessmentSeeder_CategoryEl_SetsGreekCategoryName()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var dir = TempDirWith("assessment.json", """
        { "key": "fitness-check", "title": "Fitness Check", "category": "Performance", "categoryEl": "Απόδοση", "schemaJson": "{}" }
        """);

        await AssessmentSeeder.SeedFromJsonAsync(db, dir, NullLogger.Instance);

        (await db.AssessmentDefinitions.CountAsync()).ShouldBe(1);
        var category = await db.Categories.SingleAsync();
        category.NameEn.ShouldBe("Performance");
        category.NameEl.ShouldBe("Απόδοση");
    }

    // ---- ExerciseSeeder ------------------------------------------------------

    private const string SampleExercisesJson = """
    [
      {
        "id": "3_4_Sit-Up",
        "name": "3/4 Sit-Up",
        "force": "pull",
        "level": "beginner",
        "mechanic": "compound",
        "equipment": "body only",
        "primaryMuscles": ["abdominals"],
        "secondaryMuscles": [],
        "instructions": ["Lie down on the floor.", "Raise your torso toward your knees."],
        "category": "strength",
        "images": ["3_4_Sit-Up/0.jpg", "3_4_Sit-Up/1.jpg"]
      },
      {
        "id": "Pushups",
        "name": "Pushups",
        "force": null,
        "level": "beginner",
        "mechanic": null,
        "equipment": null,
        "primaryMuscles": ["chest"],
        "secondaryMuscles": ["shoulders", "triceps"],
        "instructions": ["Lie on the floor face down."],
        "category": "strength",
        "images": ["Pushups/0.jpg", "Pushups/1.jpg"]
      }
    ]
    """;

    private static string GreekRowsJson(string slug, string nameEl) => $$"""
    [ { "slug": "{{slug}}", "nameEl": "{{nameEl}}", "instructionsEl": ["Βήμα ένα.", "Βήμα δύο."] } ]
    """;

    [Fact]
    public async Task ExerciseSeeder_ValidJson_SeedsExercisesWithSlugAndJoinedFields()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var dir = TempDirWith("exercises.json", SampleExercisesJson);

        await ExerciseSeeder.SeedFromJsonAsync(db, dir, NullLogger.Instance);

        var exercises = await db.Exercises.OrderBy(e => e.Slug).ToListAsync();
        exercises.Select(e => e.Slug).ShouldBe(["3-4-sit-up", "pushups"]);

        var sitUp = exercises[0];
        sitUp.NameEn.ShouldBe("3/4 Sit-Up");
        sitUp.NameEl.ShouldBeNull();
        sitUp.Force.ShouldBe("pull");
        sitUp.SecondaryMuscles.ShouldBe(string.Empty);
        sitUp.InstructionsEn.ShouldBe("Lie down on the floor.\nRaise your torso toward your knees.");
        sitUp.InstructionsEl.ShouldBeNull();
        sitUp.ImageCount.ShouldBe(2);
        sitUp.IsPublished.ShouldBeTrue();

        var pushups = exercises[1];
        pushups.Force.ShouldBeNull();
        pushups.Equipment.ShouldBeNull();
        pushups.PrimaryMuscles.ShouldBe("chest");
        pushups.SecondaryMuscles.ShouldBe("shoulders, triceps");
    }

    [Fact]
    public async Task ExerciseSeeder_SecondRun_IsNoOp()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var dir = TempDirWith("exercises.json", SampleExercisesJson);

        await ExerciseSeeder.SeedFromJsonAsync(db, dir, NullLogger.Instance);
        await ExerciseSeeder.SeedFromJsonAsync(db, dir, NullLogger.Instance);

        (await db.Exercises.CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task ExerciseSeeder_MissingDirectory_IsNoOp()
    {
        await using var db = DbContextFactory.CreateInMemory();

        await Should.NotThrowAsync(() => ExerciseSeeder.SeedFromJsonAsync(db, NonExistentDir(), NullLogger.Instance));

        (await db.Exercises.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task ExerciseSeeder_GreekOverlayFile_FillsGreekNameAndInstructions()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var dir = TempDirWithFiles(
            ("exercises.json", SampleExercisesJson),
            ("exercises.el.json", GreekRowsJson("pushups", "Κάμψεις")));

        await ExerciseSeeder.SeedFromJsonAsync(db, dir, NullLogger.Instance);

        var pushups = await db.Exercises.SingleAsync(e => e.Slug == "pushups");
        pushups.NameEl.ShouldBe("Κάμψεις");
        pushups.InstructionsEl.ShouldBe("Βήμα ένα.\nΒήμα δύο.");
    }

    [Fact]
    public async Task ExerciseSeeder_GreekFragmentFolder_FillsGreekNameAndInstructions()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var dir = TempDirWithFiles(
            ("exercises.json", SampleExercisesJson),
            (Path.Combine("el", "strength-basics.el.json"), GreekRowsJson("3-4-sit-up", "Κοιλιακοί 3/4")));

        await ExerciseSeeder.SeedFromJsonAsync(db, dir, NullLogger.Instance);

        var sitUp = await db.Exercises.SingleAsync(e => e.Slug == "3-4-sit-up");
        sitUp.NameEl.ShouldBe("Κοιλιακοί 3/4");
        sitUp.InstructionsEl.ShouldBe("Βήμα ένα.\nΒήμα δύο.");
    }

    [Fact]
    public async Task ExerciseSeeder_DuplicateGreekSlug_KeepsRowFromFirstFileByName()
    {
        await using var db = DbContextFactory.CreateInMemory();
        // Written in reverse order so the test proves files are read by name, not creation order.
        var dir = TempDirWithFiles(
            ("exercises.json", SampleExercisesJson),
            (Path.Combine("el", "b-course.el.json"), GreekRowsJson("pushups", "Δεύτερο")),
            (Path.Combine("el", "a-course.el.json"), GreekRowsJson("pushups", "Πρώτο")));

        await ExerciseSeeder.SeedFromJsonAsync(db, dir, NullLogger.Instance);

        var pushups = await db.Exercises.SingleAsync(e => e.Slug == "pushups");
        pushups.NameEl.ShouldBe("Πρώτο");
    }

    [Fact]
    public async Task ExerciseSeeder_UnknownGreekSlug_IsSkippedWithWarning()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var logger = new WarningRecordingLogger();
        var dir = TempDirWithFiles(
            ("exercises.json", SampleExercisesJson),
            (Path.Combine("el", "strength-basics.el.json"), GreekRowsJson("not-a-real-exercise", "Άγνωστη")));

        await ExerciseSeeder.SeedFromJsonAsync(db, dir, logger);

        (await db.Exercises.CountAsync()).ShouldBe(2);
        (await db.Exercises.AnyAsync(e => e.NameEl != null)).ShouldBeFalse();
        logger.Warnings.ShouldContain(warning => warning.Contains("not-a-real-exercise"));
    }

    // ---- StudentSeeder -------------------------------------------------------

    [Fact]
    public async Task StudentSeeder_MissingDirectory_IsNoOp()
    {
        var um = IdentityMocks.MockUserManager();

        await Should.NotThrowAsync(() =>
            StudentSeeder.SeedFromJsonAsync(um, NonExistentDir(), "Pwd-1!", NullLogger.Instance));

        await um.DidNotReceive().CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>());
    }

    // ---- BulkStudentSeedingService ------------------------------------------

    [Fact]
    public async Task BulkStudentSeedingService_NotDevelopment_IsNoOp()
    {
        var sp = Substitute.For<IServiceProvider>();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SeedData:Enabled"] = "false" })
            .Build();
        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns("Production");

        var service = new BulkStudentSeedingService(sp, config, env, NullLogger<BulkStudentSeedingService>.Instance);

        await Should.NotThrowAsync(async () =>
        {
            await service.StartAsync(CancellationToken.None);
            await service.StopAsync(CancellationToken.None);
        });

        sp.DidNotReceive().GetService(Arg.Any<Type>());
    }

    public void Dispose()
    {
        foreach (var d in _tempDirs.Where(Directory.Exists))
            Directory.Delete(d, recursive: true);
    }

    /// <summary>Keeps the text of every warning so a test can check what the seeder reported.</summary>
    private sealed class WarningRecordingLogger : ILogger
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }
}

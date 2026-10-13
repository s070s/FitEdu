using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
    public async Task SubscriptionPlanSeeder_DescribesEveryPlanInEnglishAndGreek_AndKeepsPricesAndLimits()
    {
        await using var db = DbContextFactory.CreateInMemory();

        await SubscriptionPlanSeeder.SeedAsync(db, NullLogger.Instance);

        var plans = await db.SubscriptionPlans.ToDictionaryAsync(plan => plan.Tier);
        plans.Values.ShouldAllBe(plan => !string.IsNullOrWhiteSpace(plan.Description) && !string.IsNullOrWhiteSpace(plan.DescriptionEl));
        plans[SubscriptionTier.Free].Description!.ShouldContain("exercise library");
        plans[SubscriptionTier.Plus].Description!.ShouldContain("Every Free and Plus course");
        plans[SubscriptionTier.Pro].Description!.ShouldContain("certificates");
        // Every signed-in member can join a live class, so no plan may sell them as a Pro extra.
        plans.Values.ShouldAllBe(plan => !plan.Description!.Contains("live", StringComparison.OrdinalIgnoreCase));

        // The pricing, billing and entitlement tests depend on these values.
        ShouldHavePriceAndLimits(plans[SubscriptionTier.Free], 0m, BillingPeriod.Lifetime, 1, assessments: false, certificates: false, prioritySupport: false);
        ShouldHavePriceAndLimits(plans[SubscriptionTier.Plus], 9.99m, BillingPeriod.Monthly, 10, assessments: true, certificates: false, prioritySupport: false);
        ShouldHavePriceAndLimits(plans[SubscriptionTier.Pro], 19.99m, BillingPeriod.Monthly, int.MaxValue, assessments: true, certificates: true, prioritySupport: true);
    }

    private static void ShouldHavePriceAndLimits(SubscriptionPlan plan, decimal price, BillingPeriod billingPeriod,
        int maxCourses, bool assessments, bool certificates, bool prioritySupport)
    {
        plan.Price.ShouldBe(price);
        plan.BillingPeriod.ShouldBe(billingPeriod);
        plan.IsActive.ShouldBeTrue();
        var features = System.Text.Json.JsonSerializer.Deserialize<PlanFeaturesDto>(plan.FeaturesJson!)!;
        features.MaxCourses.ShouldBe(maxCourses);
        features.AssessmentAccess.ShouldBe(assessments);
        features.CertificateAccess.ShouldBe(certificates);
        features.PrioritySupport.ShouldBe(prioritySupport);
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
    public async Task ExerciseSeeder_DuplicateGreekSlug_KeepsRowFromFirstFileByOrdinalName()
    {
        await using var db = DbContextFactory.CreateInMemory();
        // "B-" sorts before "a-" in ordinal order but after it in a case-insensitive listing (the
        // Windows directory order), and the files are written in neither order, so the test fails
        // when the seeder relies on how the file system lists the folder.
        var dir = TempDirWithFiles(
            ("exercises.json", SampleExercisesJson),
            (Path.Combine("el", "a-course.el.json"), GreekRowsJson("pushups", "Δεύτερο")),
            (Path.Combine("el", "B-course.el.json"), GreekRowsJson("pushups", "Πρώτο")));

        await ExerciseSeeder.SeedFromJsonAsync(db, dir, NullLogger.Instance);

        var pushups = await db.Exercises.SingleAsync(e => e.Slug == "pushups");
        pushups.NameEl.ShouldBe("Πρώτο");
    }

    [Fact]
    public async Task ExerciseSeeder_OverlayFileIsReadBeforeTheFragmentFolder()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var dir = TempDirWithFiles(
            ("exercises.json", SampleExercisesJson),
            (Path.Combine("el", "0-first-by-name.el.json"), GreekRowsJson("pushups", "Από το el")),
            ("exercises.el.json", GreekRowsJson("pushups", "Από το overlay")));

        await ExerciseSeeder.SeedFromJsonAsync(db, dir, NullLogger.Instance);

        (await db.Exercises.SingleAsync(e => e.Slug == "pushups")).NameEl.ShouldBe("Από το overlay");
    }

    [Fact]
    public async Task ExerciseSeeder_RowWithoutName_IsSkippedWithWarning_AndTheOtherRowsSeed()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var logger = new RecordingLogger();
        var dir = TempDirWith("exercises.json", """
        [
          { "id": "Plank", "name": "Plank", "level": "beginner", "category": "strength" },
          { "id": "No_Name", "level": "beginner", "category": "strength" },
          { "id": "Blank_Level", "name": "Blank Level", "level": " ", "category": "strength" }
        ]
        """);

        await ExerciseSeeder.SeedFromJsonAsync(db, dir, logger);

        (await db.Exercises.Select(e => e.Slug).ToListAsync()).ShouldBe(["plank"]);
        logger.Warnings.ShouldContain(warning => warning.Contains("No_Name"));
        logger.Warnings.ShouldContain(warning => warning.Contains("Blank_Level"));
    }

    [Fact]
    public async Task ExerciseSeeder_TwoIdsWithTheSameSlug_KeepsTheFirstWithWarning()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var logger = new RecordingLogger();
        var dir = TempDirWith("exercises.json", """
        [
          { "id": "Push_Up", "name": "First", "level": "beginner", "category": "strength" },
          { "id": "Push-Up", "name": "Second", "level": "beginner", "category": "strength" }
        ]
        """);

        await ExerciseSeeder.SeedFromJsonAsync(db, dir, logger);

        (await db.Exercises.SingleAsync()).NameEn.ShouldBe("First");
        logger.Warnings.ShouldContain(warning => warning.Contains("push-up"));
    }

    [Fact]
    public async Task ExerciseSeeder_GreekTextLongerThanItsColumn_IsSkippedWithWarning()
    {
        // SQL Server would refuse the whole save (SQLite and InMemory ignore the limit), so the
        // seeder keeps such a row in English instead of failing start-up.
        await using var db = DbContextFactory.CreateInMemory();
        var logger = new RecordingLogger();
        var longName = new string('α', 151);
        var longStep = new string('β', 4001);
        var dir = TempDirWithFiles(
            ("exercises.json", SampleExercisesJson),
            ("exercises.el.json", $$"""
            [
              { "slug": "pushups", "nameEl": "{{longName}}", "instructionsEl": ["Βήμα."] },
              { "slug": "3-4-sit-up", "nameEl": "Κοιλιακοί", "instructionsEl": ["{{longStep}}"] }
            ]
            """));

        await ExerciseSeeder.SeedFromJsonAsync(db, dir, logger);

        (await db.Exercises.AnyAsync(e => e.NameEl != null || e.InstructionsEl != null)).ShouldBeFalse();
        logger.Warnings.ShouldContain(warning => warning.Contains("pushups"));
        logger.Warnings.ShouldContain(warning => warning.Contains("3-4-sit-up"));
    }

    [Fact]
    public async Task ExerciseSeeder_MalformedGreekFile_IsSkippedWithError_AndTheOtherFilesApply()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var logger = new RecordingLogger();
        var dir = TempDirWithFiles(
            ("exercises.json", SampleExercisesJson),
            (Path.Combine("el", "a-broken.el.json"), "[ { \"slug\": "),
            (Path.Combine("el", "b-good.el.json"), GreekRowsJson("pushups", "Κάμψεις")));

        await ExerciseSeeder.SeedFromJsonAsync(db, dir, logger);

        (await db.Exercises.SingleAsync(e => e.Slug == "pushups")).NameEl.ShouldBe("Κάμψεις");
        logger.Errors.ShouldContain(error => error.Contains("a-broken.el.json"));
    }

    [Fact]
    public async Task ExerciseSeeder_GreekFileHoldingNull_IsSkippedWithWarning()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var logger = new RecordingLogger();
        var dir = TempDirWithFiles(("exercises.json", SampleExercisesJson), ("exercises.el.json", "null"));

        await ExerciseSeeder.SeedFromJsonAsync(db, dir, logger);

        (await db.Exercises.CountAsync()).ShouldBe(2);
        logger.Warnings.ShouldContain(warning => warning.Contains("exercises.el.json"));
    }

    [Fact]
    public async Task ExerciseSeeder_LockedGreekFile_IsSkippedWithError_AndTheLibraryStillSeeds()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var logger = new RecordingLogger();
        var dir = TempDirWithFiles(
            ("exercises.json", SampleExercisesJson),
            ("exercises.el.json", GreekRowsJson("pushups", "Κάμψεις")));

        // Another process holding the file open without sharing it makes File.OpenRead throw IOException.
        await using (File.Open(Path.Combine(dir, "exercises.el.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await ExerciseSeeder.SeedFromJsonAsync(db, dir, logger);
        }

        (await db.Exercises.CountAsync()).ShouldBe(2);
        logger.Errors.ShouldContain(error => error.Contains("exercises.el.json"));
    }

    [Fact]
    public async Task ExerciseSeeder_UnknownGreekSlug_IsSkippedWithWarning()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var logger = new RecordingLogger();
        var dir = TempDirWithFiles(
            ("exercises.json", SampleExercisesJson),
            (Path.Combine("el", "strength-basics.el.json"), GreekRowsJson("not-a-real-exercise", "Άγνωστη")));

        await ExerciseSeeder.SeedFromJsonAsync(db, dir, logger);

        (await db.Exercises.CountAsync()).ShouldBe(2);
        (await db.Exercises.AnyAsync(e => e.NameEl != null)).ShouldBeFalse();
        logger.Warnings.ShouldContain(warning => warning.Contains("not-a-real-exercise"));
    }

    // ---- LearningPathSeeder --------------------------------------------------

    private const string SamplePathsJson = """
    [
      {
        "title": "Beginner Start", "titleEl": "Ξεκίνημα για Αρχάριους",
        "description": "Start here.", "descriptionEl": "Ξεκίνα από εδώ.",
        "category": "Strength", "categoryEl": "Δύναμη",
        "displayOrder": 2, "isPublished": true,
        "courseTitles": [ "Core & Stability", "Bodyweight Basics" ]
      }
    ]
    """;

    /// <summary>Saves minimal courses with the given English titles and returns their ids in the same order.</summary>
    private static async Task<List<Guid>> AddCoursesAsync(ApplicationDbContext db, params string[] titles)
    {
        var courses = titles.Select(title => new Course { Id = Guid.NewGuid(), TitleEn = title }).ToList();
        db.Courses.AddRange(courses);
        await db.SaveChangesAsync();
        return courses.Select(course => course.Id).ToList();
    }

    [Fact]
    public async Task LearningPathSeeder_ValidJson_SeedsPublishedBilingualPathWithStepsInListOrder()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var courseIds = await AddCoursesAsync(db, "Bodyweight Basics", "Core & Stability");
        var dir = TempDirWith("paths.json", SamplePathsJson);

        await LearningPathSeeder.SeedFromJsonAsync(db, dir, NullLogger.Instance);

        var path = await db.LearningPaths.Include(p => p.Steps).Include(p => p.Category).SingleAsync();
        path.TitleEn.ShouldBe("Beginner Start");
        path.TitleEl.ShouldBe("Ξεκίνημα για Αρχάριους");
        path.DescriptionEn.ShouldBe("Start here.");
        path.DescriptionEl.ShouldBe("Ξεκίνα από εδώ.");
        path.DisplayOrder.ShouldBe(2);
        path.IsPublished.ShouldBeTrue();
        path.PublishedAt.ShouldNotBeNull();
        path.Category!.NameEn.ShouldBe("Strength");
        path.Category.NameEl.ShouldBe("Δύναμη");

        var steps = path.Steps.OrderBy(step => step.StepOrder).ToList();
        steps.Select(step => step.StepOrder).ShouldBe([1, 2]);
        steps.Select(step => step.CourseId).ShouldBe([courseIds[1], courseIds[0]]);
    }

    [Fact]
    public async Task LearningPathSeeder_ReusesTheCourseCategoryWithTheSameName()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var strength = new Category { Id = Guid.NewGuid(), NameEn = "Strength", NameEl = "Δύναμη" };
        db.Categories.Add(strength);
        await AddCoursesAsync(db, "Bodyweight Basics", "Core & Stability");
        var dir = TempDirWith("paths.json", SamplePathsJson);

        await LearningPathSeeder.SeedFromJsonAsync(db, dir, NullLogger.Instance);

        (await db.Categories.CountAsync()).ShouldBe(1);
        (await db.LearningPaths.SingleAsync()).CategoryId.ShouldBe(strength.Id);
    }

    [Fact]
    public async Task LearningPathSeeder_DuplicateCourseTitles_UsesOneOfThemInsteadOfThrowing()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var courseIds = await AddCoursesAsync(db, "Bodyweight Basics", "Bodyweight Basics", "Core & Stability");
        var dir = TempDirWith("paths.json", SamplePathsJson);

        await LearningPathSeeder.SeedFromJsonAsync(db, dir, NullLogger.Instance);

        var steps = await db.LearningPathSteps.OrderBy(step => step.StepOrder).ToListAsync();
        steps.Count.ShouldBe(2);
        steps[1].CourseId.ShouldBeOneOf(courseIds[0], courseIds[1]);
    }

    [Fact]
    public async Task LearningPathSeeder_UnknownCourseTitle_ThrowsNamingPathAndTitle()
    {
        await using var db = DbContextFactory.CreateInMemory();
        await AddCoursesAsync(db, "Bodyweight Basics");
        var dir = TempDirWith("paths.json", SamplePathsJson);

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => LearningPathSeeder.SeedFromJsonAsync(db, dir, NullLogger.Instance));

        exception.Message.ShouldContain("'Beginner Start'");
        exception.Message.ShouldContain("'Core & Stability'");
        (await db.LearningPaths.CountAsync()).ShouldBe(0); // nothing half-seeded
    }

    [Fact]
    public async Task LearningPathSeeder_WhenPathsExist_SkipsSeeding()
    {
        await using var db = DbContextFactory.CreateInMemory();
        await AddCoursesAsync(db, "Bodyweight Basics", "Core & Stability");
        db.LearningPaths.Add(new LearningPath { Id = Guid.NewGuid(), TitleEn = "Existing" });
        await db.SaveChangesAsync();
        var dir = TempDirWith("paths.json", SamplePathsJson);

        await LearningPathSeeder.SeedFromJsonAsync(db, dir, NullLogger.Instance);

        (await db.LearningPaths.SingleAsync()).TitleEn.ShouldBe("Existing");
    }

    [Fact]
    public async Task LearningPathSeeder_MissingDirectory_IsNoOp()
    {
        await using var db = DbContextFactory.CreateInMemory();

        await Should.NotThrowAsync(() => LearningPathSeeder.SeedFromJsonAsync(db, NonExistentDir(), NullLogger.Instance));

        (await db.LearningPaths.CountAsync()).ShouldBe(0);
    }

    // ---- ShowcaseSeeder ------------------------------------------------------

    private const string HostUserId = "admin-user-id";

    // Monday 12 October 2026, late in the UTC day.
    private static readonly DateTimeOffset SeedMoment = new(2026, 10, 12, 22, 30, 0, TimeSpan.Zero);

    private const string SampleTestimonialsJson = """
    [
      { "fullName": "Marcus Bennett", "roleOrTitle": "Desk worker, 42", "roleOrTitleEl": "Εργαζόμενος γραφείου, 42",
        "quoteText": "Ten minutes a day helped.", "quoteTextEl": "Δέκα λεπτά τη μέρα βοήθησαν.", "displayOrder": 2, "isActive": true },
      { "fullName": "Eleni Papadopoulou", "roleOrTitle": "Ran her first 5K", "roleOrTitleEl": "Έτρεξε τα πρώτα της 5 χλμ",
        "quoteText": "I ran 5K without stopping.", "quoteTextEl": "Έτρεξα 5 χλμ χωρίς στάση.", "displayOrder": 1, "isActive": true }
    ]
    """;

    private static string LiveClassesJson(int weekdayOffset = 1, string? courseTitle = "Mobility & Flexibility", int maxParticipants = 6) =>
        $$"""
        [
          { "title": "Live Mobility Flow", "titleEl": "Ζωντανή Ροή Κινητικότητας",
            "courseTitle": {{(courseTitle is null ? "null" : $"\"{courseTitle}\"")}},
            "weekdayOffset": {{weekdayOffset}}, "hourUtc": 18, "durationMinutes": 45, "maxParticipants": {{maxParticipants}} }
        ]
        """;

    private string ShowcaseDir(string? liveClassesJson = null) => TempDirWithFiles(
        ("testimonials.json", SampleTestimonialsJson),
        ("live-classes.json", liveClassesJson ?? LiveClassesJson()));

    private static Task SeedShowcaseAsync(ApplicationDbContext db, string dir, DateTimeOffset? now = null) =>
        ShowcaseSeeder.SeedFromJsonAsync(db, dir, HostUserId, new FixedTimeProvider(now ?? SeedMoment), NullLogger.Instance);

    [Fact]
    public async Task ShowcaseSeeder_Testimonials_AreSeededActiveAndBilingual()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var dir = ShowcaseDir(LiveClassesJson(courseTitle: null));

        await SeedShowcaseAsync(db, dir);

        var testimonials = await db.Testimonials.OrderBy(t => t.DisplayOrder).ToListAsync();
        testimonials.Select(t => t.FullName).ShouldBe(["Eleni Papadopoulou", "Marcus Bennett"]);

        var eleni = testimonials[0];
        eleni.RoleOrTitle.ShouldBe("Ran her first 5K");
        eleni.RoleOrTitleEl.ShouldBe("Έτρεξε τα πρώτα της 5 χλμ");
        eleni.QuoteText.ShouldBe("I ran 5K without stopping.");
        eleni.QuoteTextEl.ShouldBe("Έτρεξα 5 χλμ χωρίς στάση.");
        eleni.CompanyOrContext.ShouldBeNull();
        eleni.AvatarPath.ShouldBeNull();
        eleni.IsActive.ShouldBeTrue();
        eleni.Id.ShouldNotBe(Guid.Empty);
    }

    [Theory]
    [InlineData("2026-10-12", 1, "2026-10-13")] // Monday -> Tuesday
    [InlineData("2026-10-12", 5, "2026-10-19")] // Monday -> next Monday
    [InlineData("2026-10-12", 6, "2026-10-20")]
    [InlineData("2026-10-16", 1, "2026-10-19")] // Friday -> Monday
    [InlineData("2026-10-17", 1, "2026-10-19")] // Saturday -> Monday
    [InlineData("2026-10-18", 6, "2026-10-26")] // Sunday -> the Monday after next
    public async Task ShowcaseSeeder_LiveClass_StartsOnTheNthWeekdayAfterTheSeedDate_AtTheUtcHour(
        string seedDate, int weekdayOffset, string expectedDate)
    {
        await using var db = DbContextFactory.CreateInMemory();
        await AddCoursesAsync(db, "Mobility & Flexibility");
        var dir = ShowcaseDir(LiveClassesJson(weekdayOffset));
        var now = DateTimeOffset.Parse(seedDate + "T23:59:00+00:00", System.Globalization.CultureInfo.InvariantCulture);

        await SeedShowcaseAsync(db, dir, now);

        var session = await db.ScheduledSessions.SingleAsync();
        var expectedStart = DateTimeOffset.Parse(expectedDate + "T18:00:00+00:00", System.Globalization.CultureInfo.InvariantCulture);
        session.StartsAtUtc.ShouldBe(expectedStart);
        session.StartsAtUtc.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public async Task ShowcaseSeeder_LiveClass_IsHostedByTheGivenUser_AndLinkedToItsCourseByTitle()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var courseIds = await AddCoursesAsync(db, "Mobility & Flexibility");
        var dir = ShowcaseDir();

        await SeedShowcaseAsync(db, dir);

        var session = await db.ScheduledSessions.SingleAsync();
        session.HostUserId.ShouldBe(HostUserId);
        session.TitleEn.ShouldBe("Live Mobility Flow");
        session.TitleEl.ShouldBe("Ζωντανή Ροή Κινητικότητας");
        session.CourseId.ShouldBe(courseIds[0]);
        session.DurationMinutes.ShouldBe(45);
        session.MaxParticipants.ShouldBe(6);
        session.Status.ShouldBe(ScheduledSessionStatus.Scheduled);
    }

    [Fact]
    public async Task ShowcaseSeeder_LiveClassWithoutCourseTitle_HasNoCourse()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var dir = ShowcaseDir(LiveClassesJson(courseTitle: null));

        await SeedShowcaseAsync(db, dir);

        (await db.ScheduledSessions.SingleAsync()).CourseId.ShouldBeNull();
    }

    [Fact]
    public async Task ShowcaseSeeder_EachTableIsSeededOnlyWhileItIsEmpty()
    {
        await using var db = DbContextFactory.CreateInMemory();
        await AddCoursesAsync(db, "Mobility & Flexibility");
        db.Testimonials.Add(new Testimonial { Id = Guid.NewGuid(), FullName = "Real Member", QuoteText = "Written by an admin." });
        await db.SaveChangesAsync();
        var dir = ShowcaseDir();

        await SeedShowcaseAsync(db, dir);
        await SeedShowcaseAsync(db, dir);

        (await db.Testimonials.SingleAsync()).FullName.ShouldBe("Real Member");
        (await db.ScheduledSessions.CountAsync()).ShouldBe(1); // seeded by the first run only
    }

    [Fact]
    public async Task ShowcaseSeeder_UnknownCourseTitle_ThrowsNamingClassAndTitle()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var dir = ShowcaseDir(LiveClassesJson(courseTitle: "Kettlebell Basics"));

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => SeedShowcaseAsync(db, dir));

        exception.Message.ShouldContain("'Live Mobility Flow'");
        exception.Message.ShouldContain("'Kettlebell Basics'");
        (await db.ScheduledSessions.CountAsync()).ShouldBe(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)] // above the 6 people a video call holds
    public async Task ShowcaseSeeder_MaxParticipantsOutsideTheCallCap_Throws(int maxParticipants)
    {
        await using var db = DbContextFactory.CreateInMemory();
        await AddCoursesAsync(db, "Mobility & Flexibility");
        var dir = ShowcaseDir(LiveClassesJson(maxParticipants: maxParticipants));

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => SeedShowcaseAsync(db, dir));

        exception.Message.ShouldContain("'Live Mobility Flow'");
        exception.Message.ShouldContain("maxParticipants");
    }

    [Fact]
    public async Task ShowcaseSeeder_MissingDirectory_IsNoOp()
    {
        await using var db = DbContextFactory.CreateInMemory();

        await Should.NotThrowAsync(() => SeedShowcaseAsync(db, NonExistentDir()));

        (await db.Testimonials.CountAsync()).ShouldBe(0);
        (await db.ScheduledSessions.CountAsync()).ShouldBe(0);
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
}

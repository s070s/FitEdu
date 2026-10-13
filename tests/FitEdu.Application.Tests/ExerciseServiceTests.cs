using FitEdu.Application.ApiServices;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Entities;
using FitEdu.Infrastructure.Data;
using FitEdu.TestSupport;
using Shouldly;
using Xunit;

namespace FitEdu.Application.Tests;

/// <summary>
/// The public exercise library: list filters and paging, detail by slug, facet counts and the
/// lessons that prescribe an exercise. InMemory string matching is ordinal, so test values use
/// the same casing as the seeded data (the free-text search runs on SQLite, like CourseService).
/// </summary>
public class ExerciseServiceTests
{
    private static ExerciseService NewService(ApplicationDbContext db) => new(db);

    private static Exercise NewExercise(string slug, string nameEn) =>
        new()
        {
            Id = Guid.NewGuid(),
            Slug = slug,
            NameEn = nameEn,
            Category = "strength",
            Level = "beginner",
            Equipment = "body only",
            PrimaryMuscles = "chest",
            SecondaryMuscles = string.Empty,
            InstructionsEn = "Step one.\nStep two.",
            IsPublished = true
        };

    private static string Joined(IEnumerable<string> values) => string.Join("|", values);

    // ---- GetPublishedExercisesAsync -------------------------------------------

    [Fact]
    public async Task GetPublishedExercises_ReturnsOnlyPublished_OrderedByName()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var hidden = NewExercise("hidden", "Hidden");
        hidden.IsPublished = false;
        db.Exercises.AddRange(NewExercise("squat", "Squat"), NewExercise("lunge", "Lunge"), hidden);
        await db.SaveChangesAsync();

        var result = await NewService(db).GetPublishedExercisesAsync(1, 10, "en");

        result.TotalCount.ShouldBe(2);
        Joined(result.Items.Select(item => item.Name)).ShouldBe("Lunge|Squat");
    }

    [Fact]
    public async Task GetPublishedExercises_FiltersByWholePrimaryMuscleName()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var pushUp = NewExercise("pushups", "Pushups");
        pushUp.PrimaryMuscles = "chest, triceps";
        var dip = NewExercise("dips", "Dips");
        dip.PrimaryMuscles = "triceps";
        var hyperextension = NewExercise("hyperextensions", "Hyperextensions");
        hyperextension.PrimaryMuscles = "lower back";
        db.Exercises.AddRange(pushUp, dip, hyperextension);
        await db.SaveChangesAsync();
        var service = NewService(db);

        var triceps = await service.GetPublishedExercisesAsync(1, 10, "en", new ExerciseFilterDto(Muscle: "triceps"));
        var back = await service.GetPublishedExercisesAsync(1, 10, "en", new ExerciseFilterDto(Muscle: "back"));

        Joined(triceps.Items.Select(item => item.Slug)).ShouldBe("dips|pushups");
        back.TotalCount.ShouldBe(0); // "back" is not a whole muscle name, so "lower back" does not match
    }

    [Fact]
    public async Task GetPublishedExercises_FiltersByEquipmentLevelAndCategory()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var matching = NewExercise("dumbbell-press", "Dumbbell Press");
        matching.Equipment = "dumbbell";
        matching.Level = "intermediate";
        var wrongLevel = NewExercise("dumbbell-curl", "Dumbbell Curl");
        wrongLevel.Equipment = "dumbbell";
        var wrongCategory = NewExercise("dumbbell-stretch", "Dumbbell Stretch");
        wrongCategory.Equipment = "dumbbell";
        wrongCategory.Level = "intermediate";
        wrongCategory.Category = "stretching";
        db.Exercises.AddRange(matching, wrongLevel, wrongCategory, NewExercise("plank", "Plank"));
        await db.SaveChangesAsync();
        var filter = new ExerciseFilterDto(Equipment: "dumbbell", Level: "intermediate", Category: "strength");

        var result = await NewService(db).GetPublishedExercisesAsync(1, 10, "en", filter);

        result.Items.ShouldHaveSingleItem().Slug.ShouldBe("dumbbell-press");
    }

    [Fact]
    public async Task GetPublishedExercises_SearchMatchesEnglishAndGreekNames_OnSqlite()
    {
        // EF.Functions.Like is unsupported on the InMemory provider — use the relational SQLite fixture.
        await using var db = DbContextFactory.CreateSqlite();
        var squat = NewExercise("barbell-squat", "Barbell Squat");
        var greekOnly = NewExercise("lunge", "Lunge");
        greekOnly.NameEl = "Προβολή με μπάρα";
        db.Exercises.AddRange(squat, greekOnly, NewExercise("plank", "Plank"));
        await db.SaveChangesAsync();
        var service = NewService(db);

        var english = await service.GetPublishedExercisesAsync(1, 10, "en", new ExerciseFilterDto(Search: "squat"));
        var greek = await service.GetPublishedExercisesAsync(1, 10, "el", new ExerciseFilterDto(Search: "μπάρα"));

        english.Items.ShouldHaveSingleItem().Slug.ShouldBe("barbell-squat");
        greek.Items.ShouldHaveSingleItem().Name.ShouldBe("Προβολή με μπάρα");
    }

    [Fact]
    public async Task GetPublishedExercises_FiltersByWholePrimaryMuscleName_OnSqlite()
    {
        // The InMemory test above runs the predicate as LINQ to objects; this one runs the
        // concatenation + Contains through a relational provider's SQL translation.
        await using var db = DbContextFactory.CreateSqlite();
        var pushUp = NewExercise("pushups", "Pushups");
        pushUp.PrimaryMuscles = "chest, triceps";
        var dip = NewExercise("dips", "Dips");
        dip.PrimaryMuscles = "triceps";
        var hyperextension = NewExercise("hyperextensions", "Hyperextensions");
        hyperextension.PrimaryMuscles = "lower back";
        db.Exercises.AddRange(pushUp, dip, hyperextension);
        await db.SaveChangesAsync();
        var service = NewService(db);

        var triceps = await service.GetPublishedExercisesAsync(1, 10, "en", new ExerciseFilterDto(Muscle: "triceps"));
        var lowerBack = await service.GetPublishedExercisesAsync(1, 10, "en", new ExerciseFilterDto(Muscle: "lower back"));
        var back = await service.GetPublishedExercisesAsync(1, 10, "en", new ExerciseFilterDto(Muscle: "back"));

        Joined(triceps.Items.Select(item => item.Slug)).ShouldBe("dips|pushups");
        lowerBack.Items.ShouldHaveSingleItem().Slug.ShouldBe("hyperextensions");
        back.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task GetPublishedExercises_PaginatesWithCorrectTotal()
    {
        await using var db = DbContextFactory.CreateInMemory();
        db.Exercises.AddRange(NewExercise("a", "A"), NewExercise("b", "B"), NewExercise("c", "C"));
        await db.SaveChangesAsync();

        var page2 = await NewService(db).GetPublishedExercisesAsync(2, 1, "en");

        page2.TotalCount.ShouldBe(3);
        page2.Items.ShouldHaveSingleItem().Name.ShouldBe("B");
    }

    [Fact]
    public async Task GetPublishedExercises_Greek_UsesGreekNameWithEnglishFallback()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var translated = NewExercise("plank", "Plank");
        translated.NameEl = "Σανίδα";
        db.Exercises.AddRange(translated, NewExercise("squat", "Squat"));
        await db.SaveChangesAsync();

        var result = await NewService(db).GetPublishedExercisesAsync(1, 10, "el");

        Joined(result.Items.Select(item => item.Name)).ShouldBe("Σανίδα|Squat");
    }

    [Fact]
    public async Task GetPublishedExercises_ItemCarriesMusclesAndImageUrls()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var pushUp = NewExercise("pushups", "Pushups");
        pushUp.PrimaryMuscles = "chest, triceps";
        db.Exercises.Add(pushUp);
        await db.SaveChangesAsync();

        var item = (await NewService(db).GetPublishedExercisesAsync(1, 10, "en")).Items.Single();

        Joined(item.PrimaryMuscles).ShouldBe("chest|triceps");
        item.Equipment.ShouldBe("body only");
        item.Image0Url.ShouldBe("/images/exercises/pushups/0.webp");
        item.Image1Url.ShouldBe("/images/exercises/pushups/1.webp");
    }

    // ---- GetPublishedBySlugAsync ----------------------------------------------

    [Fact]
    public async Task GetPublishedBySlug_ReturnsDetailWithSplitListsAndGreekText()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var pushUp = NewExercise("pushups", "Pushups");
        pushUp.NameEl = "Κάμψεις";
        pushUp.SecondaryMuscles = "shoulders, abdominals";
        pushUp.Force = "push";
        pushUp.Mechanic = "compound";
        pushUp.InstructionsEl = "Βήμα ένα.\nΒήμα δύο.";
        db.Exercises.Add(pushUp);
        await db.SaveChangesAsync();
        var service = NewService(db);

        var english = await service.GetPublishedBySlugAsync("pushups", "en");
        var greek = await service.GetPublishedBySlugAsync("pushups", "el");

        english.ShouldNotBeNull();
        english.Name.ShouldBe("Pushups");
        Joined(english.SecondaryMuscles).ShouldBe("shoulders|abdominals");
        english.Force.ShouldBe("push");
        english.Mechanic.ShouldBe("compound");
        Joined(english.Instructions).ShouldBe("Step one.|Step two.");
        greek!.Name.ShouldBe("Κάμψεις");
        Joined(greek.Instructions).ShouldBe("Βήμα ένα.|Βήμα δύο.");
    }

    [Fact]
    public async Task GetPublishedBySlug_GreekStepsThatAreAllBlank_FallBackToTheEnglishSteps()
    {
        // A Greek column holding only separators or spaces has no step to show, so the Greek page
        // shows the English steps instead of an empty list.
        await using var db = DbContextFactory.CreateInMemory();
        var pushUp = NewExercise("pushups", "Pushups");
        pushUp.InstructionsEl = "\n  \n";
        db.Exercises.Add(pushUp);
        await db.SaveChangesAsync();

        var greek = await NewService(db).GetPublishedBySlugAsync("pushups", "el");

        Joined(greek!.Instructions).ShouldBe("Step one.|Step two.");
    }

    [Fact]
    public async Task GetPublishedBySlug_ExerciseWithoutAnyInstructions_ReturnsAnEmptyList()
    {
        // Five library exercises have no instructions at all (e.g. Iron_Cross).
        await using var db = DbContextFactory.CreateInMemory();
        var ironCross = NewExercise("iron-cross", "Iron Cross");
        ironCross.InstructionsEn = string.Empty;
        db.Exercises.Add(ironCross);
        await db.SaveChangesAsync();
        var service = NewService(db);

        (await service.GetPublishedBySlugAsync("iron-cross", "en"))!.Instructions.ShouldBeEmpty();
        (await service.GetPublishedBySlugAsync("iron-cross", "el"))!.Instructions.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetPublishedBySlug_UnknownOrUnpublished_ReturnsNull()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var hidden = NewExercise("hidden", "Hidden");
        hidden.IsPublished = false;
        db.Exercises.Add(hidden);
        await db.SaveChangesAsync();
        var service = NewService(db);

        (await service.GetPublishedBySlugAsync("missing", "en")).ShouldBeNull();
        (await service.GetPublishedBySlugAsync("hidden", "en")).ShouldBeNull();
    }

    // ---- GetFacetsAsync --------------------------------------------------------

    [Fact]
    public async Task GetFacets_CountsPublishedMusclesEquipmentLevelsAndCategories()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var pushUp = NewExercise("pushups", "Pushups");
        pushUp.PrimaryMuscles = "chest, triceps";
        var press = NewExercise("dumbbell-press", "Dumbbell Press");
        press.Equipment = "dumbbell";
        press.Level = "intermediate";
        var stretch = NewExercise("chest-stretch", "Chest Stretch");
        stretch.Equipment = null;
        stretch.Category = "stretching";
        var hidden = NewExercise("hidden", "Hidden");
        hidden.PrimaryMuscles = "neck";
        hidden.IsPublished = false;
        db.Exercises.AddRange(pushUp, press, stretch, hidden);
        await db.SaveChangesAsync();

        var facets = await NewService(db).GetFacetsAsync();

        Joined(facets.Muscles.Select(f => $"{f.Value}={f.Count}")).ShouldBe("chest=3|triceps=1");
        Joined(facets.Equipment.Select(f => $"{f.Value}={f.Count}")).ShouldBe("body only=1|dumbbell=1");
        Joined(facets.Levels.Select(f => $"{f.Value}={f.Count}")).ShouldBe("beginner=2|intermediate=1");
        Joined(facets.Categories.Select(f => $"{f.Value}={f.Count}")).ShouldBe("strength=2|stretching=1");
    }

    // ---- GetPublishedSlugsAsync ------------------------------------------------

    [Fact]
    public async Task GetPublishedSlugs_ReturnsPublishedSlugsInOrder()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var hidden = NewExercise("hidden", "Hidden");
        hidden.IsPublished = false;
        db.Exercises.AddRange(NewExercise("squat", "Squat"), NewExercise("lunge", "Lunge"), hidden);
        await db.SaveChangesAsync();

        var slugs = await NewService(db).GetPublishedSlugsAsync();

        Joined(slugs).ShouldBe("lunge|squat");
    }

    // ---- GetLessonsUsingExerciseAsync ------------------------------------------

    private static Course CourseWithLessons(string titleEn, bool published, params string[] lessonTitles)
    {
        var course = new Course { Id = Guid.NewGuid(), TitleEn = titleEn, TitleEl = titleEn + " EL", IsPublished = published };
        var module = new Module { Id = Guid.NewGuid(), TitleEn = "Module", SortOrder = 1, CourseId = course.Id };
        for (var index = 0; index < lessonTitles.Length; index++)
            module.Lessons.Add(new Lesson { Id = Guid.NewGuid(), TitleEn = lessonTitles[index], SortOrder = index + 1 });

        course.Modules.Add(module);
        return course;
    }

    private static void Prescribe(Lesson lesson, Exercise exercise, int sortOrder) =>
        lesson.Exercises.Add(new LessonExercise { Id = Guid.NewGuid(), ExerciseId = exercise.Id, SortOrder = sortOrder });

    [Fact]
    public async Task GetLessonsUsingExercise_ReturnsLessonsOfPublishedCoursesInOrder()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var pushUp = NewExercise("pushups", "Pushups");
        var squat = NewExercise("squat", "Squat");
        db.Exercises.AddRange(pushUp, squat);
        var zulu = CourseWithLessons("Zulu Course", published: true, "Zulu Lesson");
        var alpha = CourseWithLessons("Alpha Course", published: true, "Alpha Lesson 1", "Alpha Lesson 2");
        var draft = CourseWithLessons("Draft Course", published: false, "Draft Lesson");
        var alphaLessons = alpha.Modules.Single().Lessons.ToList();
        Prescribe(zulu.Modules.Single().Lessons.Single(), pushUp, 1);
        Prescribe(alphaLessons[1], pushUp, 1);
        Prescribe(alphaLessons[0], pushUp, 1);
        Prescribe(alphaLessons[0], pushUp, 2); // the same exercise twice in one lesson is listed once
        Prescribe(draft.Modules.Single().Lessons.Single(), pushUp, 1);
        Prescribe(zulu.Modules.Single().Lessons.Single(), squat, 2);
        db.Courses.AddRange(zulu, alpha, draft);
        await db.SaveChangesAsync();

        var lessons = await NewService(db).GetLessonsUsingExerciseAsync("pushups", "el");

        Joined(lessons.Select(lesson => lesson.LessonTitle)).ShouldBe("Alpha Lesson 1|Alpha Lesson 2|Zulu Lesson");
        lessons[0].CourseTitle.ShouldBe("Alpha Course EL");
        lessons[0].CourseId.ShouldBe(alpha.Id);
        lessons[0].LessonId.ShouldBe(alphaLessons[0].Id);
    }

    [Fact]
    public async Task GetLessonsUsingExercise_UnknownSlug_ReturnsEmpty()
    {
        await using var db = DbContextFactory.CreateInMemory();

        (await NewService(db).GetLessonsUsingExerciseAsync("missing", "en")).ShouldBeEmpty();
    }
}

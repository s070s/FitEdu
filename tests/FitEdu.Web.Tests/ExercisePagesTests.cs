using System.Globalization;
using Bunit;
using FitEdu.Application.DTOs;
using FitEdu.Shared.Resources;
using FitEdu.Web.Consumers;
using FitEdu.Web.Pages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// bUnit tests for the public exercise library (/exercises) and the exercise page
/// (/exercises/{slug}): cards, filters, search, pages, the empty and failure states, the
/// localized labels, the steps and the workouts that use an exercise.
/// </summary>
public class ExercisePagesTests : BunitContext
{
    private const string Slug = "barbell-squat";

    private static readonly ExerciseFacetsDto Facets = new(
        Muscles: [new("quadriceps", 40), new("chest", 30), new("abdominals", 20)],
        Equipment: [new("barbell", 50), new("body only", 40)],
        Levels: [new("beginner", 60), new("expert", 10), new("intermediate", 30)],
        Categories: [new("strength", 80), new("stretching", 20)]);

    private readonly IExerciseConsumer _exerciseConsumer = Substitute.For<IExerciseConsumer>();

    public ExercisePagesTests()
    {
        Services.AddSingleton(_exerciseConsumer);
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        AddBunitPersistentComponentState();
        UseCulture("en");

        _exerciseConsumer.GetFacetsAsync().Returns(Facets);
        _exerciseConsumer.ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<ExerciseFilterDto?>())
            .Returns(Page(totalCount: 2, ListItem("barbell-squat", "Barbell squat"), ListItem("push-ups", "Push-ups")));
    }

    // ---- Library ------------------------------------------------------------------------------

    [Fact]
    public void Library_RendersOneCardPerExerciseWithTheTotal()
    {
        var page = Render<Exercises>();

        page.FindAll(".exercise-card").Count.ShouldBe(2);
        page.Find(".exercise-card a.stretched-link").GetAttribute("href").ShouldBe("/exercises/barbell-squat");
        page.Find(".exercise-library__count").TextContent.Trim().ShouldBe("2 exercises");
        page.Find(".exercise-library__hero .eyebrow").TextContent.ShouldBe("100 exercises");
        page.Find(".exercise-source").TextContent.ShouldContain(ExerciseRes.Source);
    }

    [Fact]
    public void Library_InGreek_SortsMuscleChipsByGreekLabelAndLevelsByDifficulty()
    {
        UseCulture("el");
        string[] expectedMuscles = new[] { ExerciseRes.Muscle_quadriceps, ExerciseRes.Muscle_chest, ExerciseRes.Muscle_abdominals }
            .Order(StringComparer.Create(CultureInfo.CurrentUICulture, ignoreCase: true))
            .ToArray();

        var page = Render<Exercises>();

        var muscleChips = page.FindAll(".exercise-filters__chips .filter-chip").Skip(1)
            .Select(chip => chip.FirstChild!.TextContent.Trim())
            .ToArray();
        muscleChips.ShouldBe(expectedMuscles);
        var levelOptions = page.FindAll("#exercise-level option").Select(option => option.TextContent).ToArray();
        levelOptions.ShouldBe([
            ExerciseRes.AllLevels,
            $"{ExerciseRes.Level_beginner} (60)",
            $"{ExerciseRes.Level_intermediate} (30)",
            $"{ExerciseRes.Level_expert} (10)"
        ]);
        page.Find("#exercise-equipment option[value='body only']").TextContent.ShouldBe($"{ExerciseRes.Equipment_body_only} (40)");
    }

    [Fact]
    public void Library_MuscleChip_FiltersAndATapOnTheSelectedChipClearsIt()
    {
        var page = Render<Exercises>();

        page.Find("button.filter-chip[aria-pressed='false']:not(:first-child)").Click();

        _exerciseConsumer.Received().ListAsync(1, ExerciseLibraryPaging.DefaultPageSize, "en",
            Arg.Is<ExerciseFilterDto?>(filter => filter != null && filter.Muscle == "abdominals"));
        var selectedChip = page.Find("button.filter-chip.is-selected");
        selectedChip.GetAttribute("aria-pressed").ShouldBe("true");
        selectedChip.TextContent.ShouldContain(ExerciseRes.Muscle_abdominals);

        selectedChip.Click();

        page.Find("button.filter-chip.is-selected").TextContent.Trim().ShouldBe(ExerciseRes.AllMuscles);
    }

    [Fact]
    public void Library_Selects_FilterByLevelEquipmentAndType()
    {
        var page = Render<Exercises>();

        page.Find("#exercise-level").Change("expert");
        page.Find("#exercise-equipment").Change("barbell");
        page.Find("#exercise-category").Change("strength");

        _exerciseConsumer.Received().ListAsync(1, ExerciseLibraryPaging.DefaultPageSize, "en",
            Arg.Is<ExerciseFilterDto?>(filter => filter != null
                && filter.Level == "expert" && filter.Equipment == "barbell" && filter.Category == "strength"));
    }

    [Fact]
    public void Library_Search_WaitsForTheReaderToStopTyping()
    {
        var page = Render<Exercises>();

        page.Find(".exercise-filters__search input").Input("squ");
        page.Find(".exercise-filters__search input").Input("squat");

        page.WaitForAssertion(() => _exerciseConsumer.Received(1).ListAsync(1, ExerciseLibraryPaging.DefaultPageSize, "en",
            Arg.Is<ExerciseFilterDto?>(filter => filter != null && filter.Search == "squat")));
        _exerciseConsumer.DidNotReceive().ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(),
            Arg.Is<ExerciseFilterDto?>(filter => filter != null && filter.Search == "squ"));
    }

    [Fact]
    public void Library_NextPage_LoadsPageTwoWithTheSameFilter()
    {
        _exerciseConsumer.ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<ExerciseFilterDto?>())
            .Returns(Page(totalCount: 60, ListItem("barbell-squat", "Barbell squat")));
        var page = Render<Exercises>();

        var pageButtons = page.FindAll(".exercise-library__pages button");
        pageButtons[pageButtons.Count - 1].Click();

        _exerciseConsumer.Received().ListAsync(2, ExerciseLibraryPaging.DefaultPageSize, "en", Arg.Any<ExerciseFilterDto?>());
    }

    [Fact]
    public void Library_NoMatches_OffersToClearTheFilters()
    {
        var page = Render<Exercises>();
        _exerciseConsumer.ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(),
                Arg.Is<ExerciseFilterDto?>(filter => filter != null && filter.Level == "expert"))
            .Returns(Page(totalCount: 0));

        page.Find("#exercise-level").Change("expert");

        page.FindAll(".exercise-card").ShouldBeEmpty();
        page.Find(".empty-state__title").TextContent.ShouldBe(ExerciseRes.NoResults);
        page.Find(".empty-state button").Click();
        page.FindAll(".exercise-card").Count.ShouldBe(2);
        page.FindAll(".exercise-library__clear").ShouldBeEmpty();
    }

    [Fact]
    public void Library_EmptyWithoutFilters_ShowsTheLoadFailureWithTryAgain()
    {
        _exerciseConsumer.ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<ExerciseFilterDto?>())
            .Returns(Page(totalCount: 0));

        var page = Render<Exercises>();

        page.Find(".empty-state__title").TextContent.ShouldBe(ExerciseRes.LoadFailed);
        page.Find(".empty-state button").TextContent.ShouldBe(GlobalRes.TryAgain);
    }

    // ---- Exercise page ------------------------------------------------------------------------

    [Fact]
    public void Detail_RendersPhotosFactsMusclesStepsWorkoutsAndCredit()
    {
        var courseId = Guid.NewGuid();
        _exerciseConsumer.GetBySlugAsync(Slug, "en").Returns(Detail(["Stand tall.", " ", "Sit back and down."]));
        _exerciseConsumer.GetLessonsUsingExerciseAsync(Slug, "en")
            .Returns([new LessonUsingExerciseDto(Guid.NewGuid(), "Leg day", courseId, "Strength basics")]);

        var page = RenderDetail();

        page.Find("h1").TextContent.ShouldBe("Barbell squat");
        page.FindAll(".exercise-media img").Count.ShouldBe(2);
        page.FindAll(".exercise-media img").ShouldAllBe(image => image.GetAttribute("loading") == "eager");
        page.Find(".level-badge").TextContent.ShouldContain(ExerciseRes.Level_beginner);
        page.FindAll(".exercise-facts__item dd").Select(value => value.TextContent).ShouldBe([
            ExerciseRes.Equipment_barbell, ExerciseRes.Force_push, ExerciseRes.Mechanic_compound, ExerciseRes.Category_strength
        ]);
        page.FindAll(".muscle-chips").Count.ShouldBe(2);
        page.FindAll(".exercise-steps li").Select(step => step.TextContent).ShouldBe(["Stand tall.", "Sit back and down."]);
        var workout = page.Find(".used-in-list a");
        workout.GetAttribute("href").ShouldBe($"/courses/{courseId}");
        workout.TextContent.ShouldContain("Leg day");
        workout.TextContent.ShouldContain("Strength basics");
        page.Find(".exercise-source").TextContent.ShouldContain(ExerciseRes.Source);
    }

    [Fact]
    public void Detail_InGreek_ShowsGreekLabelsAndAsksForGreekContent()
    {
        UseCulture("el");
        _exerciseConsumer.GetBySlugAsync(Slug, "el").Returns(Detail(["Στάσου όρθιος."]));
        _exerciseConsumer.GetLessonsUsingExerciseAsync(Slug, "el").Returns([]);

        var page = RenderDetail();

        page.Find(".exercise-detail__header .eyebrow").TextContent.ShouldBe(ExerciseRes.Category_strength);
        page.Find(".exercise-facts__item dd").TextContent.ShouldBe(ExerciseRes.Equipment_barbell);
        page.Find(".muscle-chips .chip").TextContent.ShouldBe(ExerciseRes.Muscle_quadriceps);
    }

    [Fact]
    public void Detail_WithoutInstructions_ShowsANoteInsteadOfAnEmptyList()
    {
        _exerciseConsumer.GetBySlugAsync(Slug, "en").Returns(Detail([]));
        _exerciseConsumer.GetLessonsUsingExerciseAsync(Slug, "en").Returns([]);

        var page = RenderDetail();

        page.FindAll(".exercise-steps").ShouldBeEmpty();
        page.Find(".exercise-steps__empty").TextContent.ShouldContain(ExerciseRes.NoInstructions);
        page.FindAll(".used-in-list").ShouldBeEmpty();
    }

    [Fact]
    public void Detail_WithoutForceOrMechanics_LeavesThoseTilesOut()
    {
        _exerciseConsumer.GetBySlugAsync(Slug, "en").Returns(Detail([]) with { Force = null, Mechanic = null, Equipment = null });
        _exerciseConsumer.GetLessonsUsingExerciseAsync(Slug, "en").Returns([]);

        var page = RenderDetail();

        page.FindAll(".exercise-facts__item dt").Select(label => label.TextContent.Trim())
            .ShouldBe([ExerciseRes.Category]);
    }

    [Fact]
    public void Detail_UnknownSlug_ShowsNotFoundWithALinkToTheLibrary()
    {
        _exerciseConsumer.GetBySlugAsync(Slug, "en").Returns((ExerciseDetailDto?)null);
        _exerciseConsumer.GetLessonsUsingExerciseAsync(Slug, "en").Returns([]);

        var page = RenderDetail();

        page.Find(".empty-state__title").TextContent.ShouldBe(ExerciseRes.NotFoundTitle);
        page.Find(".empty-state a").GetAttribute("href").ShouldBe("/exercises");
    }

    // ---- Helpers ------------------------------------------------------------------------------

    private IRenderedComponent<ExerciseDetail> RenderDetail() =>
        Render<ExerciseDetail>(parameters => parameters.Add(component => component.Slug, Slug));

    private static void UseCulture(string culture)
    {
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
    }

    private static PagedResult<ExerciseListItemDto> Page(int totalCount, params ExerciseListItemDto[] items) =>
        new([.. items], totalCount, Page: 1, ExerciseLibraryPaging.DefaultPageSize);

    private static ExerciseListItemDto ListItem(string slug, string name) => new(
        Guid.NewGuid(), slug, name, "strength", "beginner", "barbell", ["quadriceps"],
        $"/images/exercises/{slug}/0.webp", $"/images/exercises/{slug}/1.webp");

    private static ExerciseDetailDto Detail(string[] instructions) => new(
        Guid.NewGuid(), Slug, "Barbell squat", "strength", "beginner", "barbell", ["quadriceps"],
        $"/images/exercises/{Slug}/0.webp", $"/images/exercises/{Slug}/1.webp",
        SecondaryMuscles: ["glutes", "hamstrings"], Force: "push", Mechanic: "compound", instructions);
}

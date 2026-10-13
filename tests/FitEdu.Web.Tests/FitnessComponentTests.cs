using System.Globalization;
using System.Text.RegularExpressions;
using Bunit;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Enums;
using FitEdu.TestSupport;
using FitEdu.Web.Extensions;
using FitEdu.Web.Shared.Components.Fitness;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using WebErrorEventArgs = Microsoft.AspNetCore.Components.Web.ErrorEventArgs;

namespace FitEdu.Web.Tests;

/// <summary>
/// The fitness building blocks (Shared/Components/Fitness): exercise photos and cards, level and
/// muscle chips, progress ring, stat tile and course cover. Each renders in English and Greek and
/// falls back sensibly when data is missing.
/// </summary>
public class FitnessComponentTests : BunitContext
{
    private const string StartUrl = "/images/exercises/bodyweight-squat/0.webp";
    private const string EndUrl = "/images/exercises/bodyweight-squat/1.webp";

    private static readonly string ExerciseMediaCssPath = Path.Combine(
        RepoPaths.RepoRoot, "src", "FitEdu.Web", "Shared", "Components", "Fitness", "ExerciseMedia.razor.css");

    public FitnessComponentTests()
    {
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build()); // _Imports.razor injects it everywhere
    }

    // ---- ExerciseMedia ------------------------------------------------------------------------

    [Fact]
    public void ExerciseMedia_RendersBothFramesLazyWithPositionAltText()
    {
        UseCulture("en");

        var media = RenderMedia(interactive: true);

        var frames = media.FindAll("img.exercise-media__frame");
        frames.Count.ShouldBe(2);
        frames[0].GetAttribute("src").ShouldBe(StartUrl);
        frames[0].GetAttribute("alt").ShouldBe("Bodyweight squat — start position");
        frames[1].GetAttribute("src").ShouldBe(EndUrl);
        frames[1].GetAttribute("alt").ShouldBe("Bodyweight squat — finish position");
        frames.ShouldAllBe(frame => frame.GetAttribute("loading") == "lazy"
            && frame.GetAttribute("width") == "480" && frame.GetAttribute("height") == "320");
        // The decorative phase tags are not a figcaption: the photos' alt text already names them.
        media.FindAll("figcaption").ShouldBeEmpty();
        media.Find(".exercise-media__phases").GetAttribute("aria-hidden").ShouldBe("true");
    }

    [Fact]
    public void ExerciseMedia_Tap_PausesAndResumesTheLoop()
    {
        UseCulture("en");
        var media = RenderMedia(interactive: true);
        media.Find("figure").ClassList.ShouldContain("exercise-media--loop");

        media.Find(".exercise-media__toggle").Click();

        media.Find("figure").ClassList.ShouldContain("exercise-media--paused");
        media.Find(".exercise-media__toggle").GetAttribute("aria-pressed").ShouldBe("true");
        media.Find(".exercise-media__toggle .bi").ClassList.ShouldContain("bi-play-fill");

        media.Find(".exercise-media__toggle").Click();

        media.Find("figure").ClassList.ShouldNotContain("exercise-media--paused");
        media.Find(".exercise-media__toggle").GetAttribute("aria-pressed").ShouldBe("false");
    }

    [Fact]
    public void ExerciseMedia_NotInteractive_IsAStillPhotoWithNoLoopOrPauseButton()
    {
        var media = RenderMedia(interactive: false);

        var figure = media.Find("figure");
        figure.ClassList.ShouldContain("exercise-media--still");
        figure.ClassList.ShouldNotContain("exercise-media--loop");
        media.FindAll(".exercise-media__toggle").ShouldBeEmpty();
        media.FindAll("img.exercise-media__frame").Count.ShouldBe(2); // the finish photo stays for the hover preview
    }

    [Fact]
    public void ExerciseMediaCss_OnlyTheLoopModeAnimatesForever()
    {
        // WCAG 2.2.2 (pause, stop, hide): a grid of still library cards must never loop.
        var endlessRules = Regex.Matches(ExerciseMediaCss(), @"([^{}]+)\{[^{}]*\binfinite\b[^{}]*\}");

        endlessRules.ShouldNotBeEmpty();
        var selectors = endlessRules.SelectMany(rule => rule.Groups[1].Value.Split(','));
        selectors.ShouldAllBe(selector => selector.Contains(".exercise-media--loop"));
    }

    [Fact]
    public void ExerciseMediaCss_PauseRoundsAHalfDoneFadeToAWholePhoto()
    {
        var pausedRule = Regex.Match(ExerciseMediaCss(), @"\.exercise-media--paused \.exercise-media__frame--end[^{]*\{([^}]*)\}");

        pausedRule.Success.ShouldBeTrue();
        pausedRule.Groups[1].Value.ShouldContain("animation-play-state: paused");
        pausedRule.Groups[1].Value.ShouldContain("steps(2, jump-none)");
    }

    [Fact]
    public void ExerciseMedia_Greek_UsesGreekAltTextPhasesAndButtonName()
    {
        UseCulture("el");

        var media = RenderMedia(interactive: true, name: "Βαθύ κάθισμα");

        media.Find(".exercise-media__frame--start").GetAttribute("alt").ShouldBe("Βαθύ κάθισμα — αρχική θέση");
        media.Find(".exercise-media__frame--end").GetAttribute("alt").ShouldBe("Βαθύ κάθισμα — τελική θέση");
        media.Find(".exercise-media__phase--start").TextContent.ShouldBe("Αρχή");
        media.Find(".exercise-media__phase--end").TextContent.ShouldBe("Τέλος");
        media.Find(".exercise-media__toggle").GetAttribute("aria-label").ShouldBe("Παύση κίνησης");
    }

    [Fact]
    public void ExerciseMedia_Eager_LoadsBothFramesEagerly()
    {
        var media = Render<ExerciseMedia>(parameters => parameters
            .Add(component => component.StartUrl, StartUrl)
            .Add(component => component.EndUrl, EndUrl)
            .Add(component => component.Name, "Squat")
            .Add(component => component.Eager, true));

        media.FindAll("img").ShouldAllBe(frame => frame.GetAttribute("loading") == "eager");
    }

    // ---- ExerciseCard -------------------------------------------------------------------------

    [Fact]
    public void ExerciseCard_LibraryExercise_IsOneLinkWithLevelEquipmentAndTwoMuscles()
    {
        UseCulture("en");

        var card = Render<ExerciseCard>(parameters => parameters
            .Add(component => component.Exercise, LibraryExercise()));

        card.Find("article").ClassList.ShouldContain("exercise-card--library");
        card.Find("figure").ClassList.ShouldContain("exercise-media--still"); // a grid of cards never loops
        var link = card.Find("a.exercise-card__link");
        link.GetAttribute("href").ShouldBe("/exercises/bodyweight-squat");
        link.ClassList.ShouldContain("stretched-link");
        card.Find(".exercise-card__name").TextContent.Trim().ShouldBe("Bodyweight squat");
        card.Find(".level-badge").TextContent.ShouldContain("Beginner");
        card.Find(".exercise-card__equipment").TextContent.ShouldContain("Bodyweight");
        var muscleChips = card.FindAll(".muscle-chips li");
        muscleChips.Select(chip => chip.TextContent).ShouldBe(["Quadriceps", "Glutes", "+1 more"]);
        card.FindAll(".exercise-media__toggle").ShouldBeEmpty();
        card.FindAll(".exercise-card__dose").ShouldBeEmpty();
    }

    [Fact]
    public void ExerciseCard_Prescription_ShowsPositionDoseNoteAndSteps()
    {
        UseCulture("en");

        var card = Render<ExerciseCard>(parameters => parameters
            .Add(component => component.Prescription, Prescription())
            .Add(component => component.Position, 1));

        card.Find("article").ClassList.ShouldContain("exercise-card--workout");
        card.Find("figure").ClassList.ShouldContain("exercise-media--loop");
        card.Find(".exercise-card__position").TextContent.ShouldBe("01");
        var link = card.Find("a.exercise-card__link");
        link.GetAttribute("href").ShouldBe("/exercises/bodyweight-squat");
        link.ClassList.ShouldNotContain("stretched-link"); // the pause button and the steps stay usable
        card.FindAll(".exercise-media__toggle").Count.ShouldBe(1);
        card.FindAll(".exercise-card__dose dt").Select(label => label.TextContent).ShouldBe(["Sets", "Reps", "Rest"]);
        card.FindAll(".exercise-card__dose dd").Select(value => value.TextContent).ShouldBe(["3", "10–12", "60 s"]);
        card.Find(".exercise-card__note").TextContent.ShouldContain("Keep your heels down.");
        card.Find(".exercise-card__steps summary").TextContent.ShouldContain("How to do it");
        card.FindAll(".exercise-card__steps li").Count.ShouldBe(2);
        card.FindAll(".muscle-chips li").Count.ShouldBe(3); // a workout card lists every muscle
        card.FindAll(".level-badge").ShouldBeEmpty(); // prescriptions carry no level
    }

    [Fact]
    public void ExerciseCard_PrescriptionInGreek_ShowsGreekLabels()
    {
        UseCulture("el");

        var card = Render<ExerciseCard>(parameters => parameters
            .Add(component => component.Prescription, Prescription()));

        card.FindAll(".muscle-chips li").Select(chip => chip.TextContent).ShouldBe(["Τετρακέφαλοι", "Γλουτοί", "Οπίσθιοι μηριαίοι"]);
        card.Find(".exercise-card__equipment").TextContent.ShouldContain("Βάρος σώματος");
        card.FindAll(".exercise-card__dose dt").Select(label => label.TextContent).ShouldBe(["Σετ", "Επαναλήψεις", "Διάλειμμα"]);
        card.FindAll(".exercise-card__dose dd")[2].TextContent.ShouldBe("60 δευτ.");
        card.Find(".exercise-card__steps summary").TextContent.ShouldContain("Πώς γίνεται");
        card.FindAll(".exercise-card__position").ShouldBeEmpty();
    }

    [Fact]
    public void ExerciseCard_PrescriptionWithoutDoseNotesOrSteps_ShowsOnlyTheExercise()
    {
        var bare = Prescription() with { Sets = null, Reps = " ", RestSeconds = null, Notes = null, Instructions = [] };

        var card = Render<ExerciseCard>(parameters => parameters
            .Add(component => component.Prescription, bare));

        card.FindAll(".exercise-card__dose").ShouldBeEmpty();
        card.FindAll(".exercise-card__note").ShouldBeEmpty();
        card.FindAll(".exercise-card__steps").ShouldBeEmpty();
        card.FindAll(".exercise-card__name").Count.ShouldBe(1);
    }

    [Fact]
    public void ExerciseCard_WithoutAnExercise_RendersNothing()
    {
        var card = Render<ExerciseCard>();

        card.Markup.Trim().ShouldBeEmpty();
    }

    // ---- LevelBadge ---------------------------------------------------------------------------

    [Theory]
    [InlineData(CourseLevel.Beginner, "en", "chip--teal", "bi-reception-2", "Beginner")]
    [InlineData(CourseLevel.Intermediate, "en", "chip--lime", "bi-reception-3", "Intermediate")]
    [InlineData(CourseLevel.Advanced, "el", "chip--coral", "bi-reception-4", "Προχωρημένο")]
    public void LevelBadge_CourseLevel_ShowsItsColourIconAndLabel(
        CourseLevel level, string culture, string chipClass, string icon, string label)
    {
        UseCulture(culture);

        var badge = Render<LevelBadge>(parameters => parameters.Add(component => component.Level, level));

        badge.Find(".level-badge").ClassList.ShouldContain(chipClass);
        badge.Find(".level-badge .bi").ClassList.ShouldContain(icon);
        badge.Find(".level-badge").TextContent.Trim().ShouldEndWith(label);
    }

    [Theory]
    [InlineData("beginner", "el", "chip--teal", "Αρχάριο")]
    [InlineData("intermediate", "el", "chip--lime", "Μεσαίο")]
    [InlineData("expert", "en", "chip--coral", "Advanced")]
    [InlineData("expert", "el", "chip--coral", "Προχωρημένο")]
    public void LevelBadge_ExerciseLevel_ShowsItsColourAndLabel(string level, string culture, string chipClass, string label)
    {
        UseCulture(culture);

        var badge = Render<LevelBadge>(parameters => parameters.Add(component => component.ExerciseLevel, level));

        badge.Find(".level-badge").ClassList.ShouldContain(chipClass);
        badge.Find(".level-badge").TextContent.Trim().ShouldEndWith(label);
    }

    [Fact]
    public void LevelBadge_NamesTheLevelForScreenReaders()
    {
        UseCulture("el");

        var badge = Render<LevelBadge>(parameters => parameters.Add(component => component.ExerciseLevel, "beginner"));

        badge.Find(".level-badge .visually-hidden").TextContent.ShouldBe("Επίπεδο: ");
    }

    [Fact]
    public void LevelBadge_UnknownExerciseLevel_ShowsTheRawValueOnANeutralChip()
    {
        var badge = Render<LevelBadge>(parameters => parameters.Add(component => component.ExerciseLevel, "elite"));

        var chip = badge.Find(".level-badge");
        chip.ClassList.ShouldNotContain("chip--teal");
        chip.ClassList.ShouldNotContain("chip--lime");
        chip.ClassList.ShouldNotContain("chip--coral");
        chip.TextContent.Trim().ShouldEndWith("elite");
    }

    [Fact]
    public void LevelBadge_NoLevel_RendersNothing()
    {
        var badge = Render<LevelBadge>(parameters => parameters.Add(component => component.ExerciseLevel, " "));

        badge.Markup.Trim().ShouldBeEmpty();
    }

    // ---- MuscleChips --------------------------------------------------------------------------

    [Fact]
    public void MuscleChips_Greek_LabelsEveryMuscleAndNamesTheList()
    {
        UseCulture("el");

        var chips = Render<MuscleChips>(parameters => parameters
            .Add(component => component.Muscles, ["lower back", "quadriceps"]));

        chips.FindAll("li").Select(chip => chip.TextContent).ShouldBe(["Κάτω πλάτη", "Τετρακέφαλοι"]);
        chips.Find("ul").GetAttribute("aria-label").ShouldBe("Κύριοι μύες");
        chips.FindAll("li.muscle-chips__chip--secondary").ShouldBeEmpty();
    }

    [Fact]
    public void MuscleChips_Max_CollapsesTheRestIntoAMoreChip()
    {
        UseCulture("el");

        var chips = Render<MuscleChips>(parameters => parameters
            .Add(component => component.Muscles, ["chest", "shoulders", "triceps", "abdominals"])
            .Add(component => component.Max, 2));

        chips.FindAll("li").Select(chip => chip.TextContent).ShouldBe(["Στήθος", "Ώμοι", "+2 ακόμη"]);
    }

    [Fact]
    public void MuscleChips_Secondary_UsesOutlinedChipsAndTheSecondaryListName()
    {
        UseCulture("en");

        var chips = Render<MuscleChips>(parameters => parameters
            .Add(component => component.Muscles, ["calves"])
            .Add(component => component.Secondary, true));

        chips.Find("ul").GetAttribute("aria-label").ShouldBe("Secondary muscles");
        chips.FindAll("li.muscle-chips__chip--secondary").Count.ShouldBe(1);
    }

    [Fact]
    public void MuscleChips_NoMuscles_RendersNothing()
    {
        var chips = Render<MuscleChips>(parameters => parameters.Add(component => component.Muscles, [" ", ""]));

        chips.Markup.Trim().ShouldBeEmpty();
    }

    // ---- ProgressRing -------------------------------------------------------------------------

    [Theory]
    [InlineData(40, "40", "60")]
    [InlineData(150, "100", "0")]
    [InlineData(-5, "0", "100")]
    public void ProgressRing_ClampsTheValueForTheMeterAndTheArc(int value, string expectedNow, string expectedOffset)
    {
        var ring = Render<ProgressRing>(parameters => parameters.Add(component => component.Value, value));

        ring.Find("[role=progressbar]").GetAttribute("aria-valuenow").ShouldBe(expectedNow);
        ring.Find(".progress-ring__arc").GetAttribute("stroke-dashoffset").ShouldBe(expectedOffset);
    }

    [Fact]
    public void ProgressRing_ShowsThePercentageAndADefaultName()
    {
        UseCulture("en");

        var ring = Render<ProgressRing>(parameters => parameters.Add(component => component.Value, 40));

        ring.Find(".progress-ring__text").TextContent.ShouldBe("40%");
        ring.Find("[role=progressbar]").GetAttribute("aria-label").ShouldBe("Progress");
    }

    [Fact]
    public void ProgressRing_LabelSizeAndName_ReplaceTheDefaults()
    {
        UseCulture("el");

        var ring = Render<ProgressRing>(parameters => parameters
            .Add(component => component.Value, 100)
            .Add(component => component.Label, "5/5")
            .Add(component => component.Size, 72)
            .Add(component => component.AriaLabel, "5 από 5 ολοκληρώθηκαν"));

        var meter = ring.Find("[role=progressbar]");
        meter.GetAttribute("aria-label").ShouldBe("5 από 5 ολοκληρώθηκαν");
        var style = meter.GetAttribute("style");
        style.ShouldNotBeNull();
        style.ShouldContain("--progress-ring-size: 72px");
        meter.ClassList.ShouldContain("progress-ring--complete");
        ring.Find(".progress-ring__text").TextContent.ShouldBe("5/5");
    }

    // ---- StatTile -----------------------------------------------------------------------------

    [Fact]
    public void StatTile_ShowsValueLabelAndIcon()
    {
        var tile = Render<StatTile>(parameters => parameters
            .Add(component => component.Value, "12")
            .Add(component => component.Label, "Προπονήσεις")
            .Add(component => component.Icon, "bi-fire"));

        tile.Find(".stat-tile__value").TextContent.ShouldBe("12");
        tile.Find(".stat-tile__label").TextContent.ShouldBe("Προπονήσεις");
        tile.Find(".stat-tile__icon").ClassList.ShouldContain("bi-fire");
    }

    [Fact]
    public void StatTile_WithoutIcon_ShowsNoIcon()
    {
        var tile = Render<StatTile>(parameters => parameters
            .Add(component => component.Value, "870")
            .Add(component => component.Label, "Exercises"));

        tile.FindAll(".stat-tile__icon").ShouldBeEmpty();
    }

    // ---- CourseCover --------------------------------------------------------------------------

    [Fact]
    public void CourseCover_WithPhoto_ShowsTheDuotoneLazyPhoto()
    {
        var cover = Render<CourseCover>(parameters => parameters
            .Add(component => component.ImageUrl, StartUrl)
            .Add(component => component.CategoryName, "Strength"));

        cover.Find(".course-cover").ClassList.ShouldContain("duotone");
        var photo = cover.Find("img.course-cover__photo");
        photo.GetAttribute("src").ShouldBe(StartUrl);
        photo.GetAttribute("alt").ShouldBe(string.Empty);
        photo.GetAttribute("loading").ShouldBe("lazy");
        cover.FindAll(".course-cover__icon").ShouldBeEmpty();
    }

    [Fact]
    public void CourseCover_PhotoFailsToLoad_FallsBackToTheCategoryIcon()
    {
        var cover = Render<CourseCover>(parameters => parameters
            .Add(component => component.ImageUrl, "/images/exercises/missing/0.webp")
            .Add(component => component.CategoryName, "Cardio"));

        cover.Find("img").TriggerEvent("onerror", new WebErrorEventArgs());

        cover.FindAll("img").ShouldBeEmpty();
        cover.Find(".course-cover").ClassList.ShouldContain("course-cover--icon");
        cover.Find(".course-cover__icon").ClassList.ShouldContain("bi-heart-pulse");
    }

    [Theory]
    [InlineData("Δύναμη", "bi-lightning-charge")]
    [InlineData("Unknown category", CategoryIcons.Fallback)]
    [InlineData(null, CategoryIcons.Fallback)]
    public void CourseCover_NoPhoto_ShowsTheCategoryIcon(string? categoryName, string expectedIcon)
    {
        var cover = Render<CourseCover>(parameters => parameters
            .Add(component => component.CategoryName, categoryName));

        cover.FindAll("img").ShouldBeEmpty();
        cover.Find(".course-cover__icon").ClassList.ShouldContain(expectedIcon);
        cover.Find(".course-cover__icon").GetAttribute("aria-hidden").ShouldBe("true");
    }

    [Fact]
    public void CourseCover_ChildContent_SitsInTheOverlay()
    {
        var cover = Render<CourseCover>(parameters => parameters
            .AddChildContent("<span class=\"badge-under-test\">Plus</span>"));

        cover.Find(".course-cover__overlay .badge-under-test").TextContent.ShouldBe("Plus");
    }

    // ---- CategoryIcons ------------------------------------------------------------------------

    [Theory]
    [InlineData("Strength", "Δύναμη")]
    [InlineData("Cardio", "Κάρντιο")]
    [InlineData("Mobility", "Κινητικότητα")]
    [InlineData("Core", "Κορμός")]
    [InlineData("Recovery", "Αποκατάσταση")]
    [InlineData("Nutrition", "Διατροφή")]
    [InlineData("Performance", "Απόδοση")]
    [InlineData("Mindset", "Νοοτροπία")]
    [InlineData("Readiness", "Ετοιμότητα")]
    [InlineData("Safety", "Ασφάλεια")]
    [InlineData("Setup", "Προετοιμασία")]
    public void CategoryIcons_EnglishAndGreekNames_ShareOneSpecificIcon(string englishName, string greekName)
    {
        var icon = CategoryIcons.For(englishName);

        icon.ShouldNotBe(CategoryIcons.Fallback);
        CategoryIcons.For(greekName).ShouldBe(icon);
        CategoryIcons.For(" " + englishName.ToUpperInvariant() + " ").ShouldBe(icon);
    }

    // ---- Helpers ------------------------------------------------------------------------------

    private static void UseCulture(string culture)
    {
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
    }

    /// <summary>ExerciseMedia.razor.css without its comments.</summary>
    private static string ExerciseMediaCss() =>
        Regex.Replace(File.ReadAllText(ExerciseMediaCssPath), @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

    private IRenderedComponent<ExerciseMedia> RenderMedia(bool interactive, string name = "Bodyweight squat") =>
        Render<ExerciseMedia>(parameters => parameters
            .Add(component => component.StartUrl, StartUrl)
            .Add(component => component.EndUrl, EndUrl)
            .Add(component => component.Name, name)
            .Add(component => component.Interactive, interactive));

    private static ExerciseListItemDto LibraryExercise() => new(
        Guid.NewGuid(),
        "bodyweight-squat",
        "Bodyweight squat",
        "strength",
        "beginner",
        "body only",
        ["quadriceps", "glutes", "hamstrings"],
        StartUrl,
        EndUrl);

    private static LessonExerciseDto Prescription() => new(
        Guid.NewGuid(),
        "bodyweight-squat",
        "Bodyweight squat",
        "body only",
        ["quadriceps", "glutes", "hamstrings"],
        Sets: 3,
        Reps: "10–12",
        RestSeconds: 60,
        Notes: "Keep your heels down.",
        StartUrl,
        EndUrl,
        Instructions: ["Stand tall with your feet shoulder-width apart.", "Sit back and down, then stand up."]);
}

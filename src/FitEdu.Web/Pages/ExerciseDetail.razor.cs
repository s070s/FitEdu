using System.Globalization;
using Microsoft.AspNetCore.Components;
using FitEdu.Application.DTOs;
using FitEdu.Shared.Resources;
using FitEdu.Web.Consumers;
using FitEdu.Web.Extensions;

namespace FitEdu.Web.Pages;

/// <summary>
/// Public page of one library exercise: the looping start/finish photos, level, equipment, force
/// and mechanics, the muscles, the steps and the workouts (lessons) that use it.
/// </summary>
public partial class ExerciseDetail : IDisposable
{
    private const string PersistKeyPrefix = "exercise-detail-";
    private const string CoursePagePrefix = "/courses/";

    [Parameter] public string Slug { get; set; } = string.Empty;

    [Inject] private IExerciseConsumer ExerciseConsumer { get; set; } = default!;
    [Inject] private PersistentComponentState ApplicationState { get; set; } = default!;

    // Present only while prerendering, so a missing item can answer 404.
    [CascadingParameter] private HttpContext? HttpContext { get; set; }

    private ExerciseDetailDto? _exercise;
    private IReadOnlyList<LessonUsingExerciseDto> _lessons = [];
    private List<ExerciseFact> _facts = [];
    private string? _loadedSlug;
    private bool _loading = true;
    private PersistingComponentStateSubscription _persistSubscription;

    private static string CurrentLang =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "el" ? "el" : "en";

    private string PersistKey => PersistKeyPrefix + Slug;

    private string CanonicalPath => "/exercises/" + _exercise?.Slug;

    protected override void OnInitialized()
    {
        _persistSubscription = ApplicationState.RegisterOnPersisting(PersistSnapshot);
    }

    protected override async Task OnParametersSetAsync()
    {
        if (Slug == _loadedSlug)
            return;

        _loadedSlug = Slug;
        _loading = true;

        if (ApplicationState.TryTakeFromJson<DetailSnapshot>(PersistKey, out var snapshot) && snapshot is not null)
        {
            Show(snapshot.Exercise, snapshot.Lessons);
            return;
        }

        var exerciseTask = ExerciseConsumer.GetBySlugAsync(Slug, CurrentLang);
        var lessonsTask = ExerciseConsumer.GetLessonsUsingExerciseAsync(Slug, CurrentLang);
        await Task.WhenAll(exerciseTask, lessonsTask);

        // A failed request still shows the "not found" view, as before; only the admin editor needs the difference.
        Show((await exerciseTask).Item, await lessonsTask);
    }

    private void Show(ExerciseDetailDto? exercise, IReadOnlyList<LessonUsingExerciseDto> lessons)
    {
        _exercise = exercise;
        _lessons = lessons;
        _facts = exercise is null ? [] : BuildFacts(exercise);
        _loading = false;

        if (exercise is null)
            PrerenderStatus.SetNotFound(HttpContext);
    }

    /// <summary>
    /// The "at a glance" tiles; a value the source data leaves empty gets no tile. The category is
    /// the page's eyebrow, so it has no tile of its own.
    /// </summary>
    private static List<ExerciseFact> BuildFacts(ExerciseDetailDto exercise)
    {
        ExerciseFact[] candidates =
        [
            new("bi-tools", ExerciseRes.Equipment, ExerciseLabels.Equipment(exercise.Equipment)),
            new("bi-arrow-left-right", ExerciseRes.Force, ExerciseLabels.Force(exercise.Force)),
            new("bi-diagram-3", ExerciseRes.Mechanic, ExerciseLabels.Mechanic(exercise.Mechanic))
        ];

        return candidates.Where(fact => !string.IsNullOrWhiteSpace(fact.Value)).ToList();
    }

    private static string CourseUrl(LessonUsingExerciseDto lesson) => CoursePagePrefix + lesson.CourseId;

    private Task PersistSnapshot()
    {
        if (!_loading)
            ApplicationState.PersistAsJson(PersistKey, new DetailSnapshot(_exercise, _lessons.ToList()));

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _persistSubscription.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>One "at a glance" tile: a Bootstrap icon, a label and the value in the page language.</summary>
    private sealed record ExerciseFact(string Icon, string Label, string Value);

    /// <summary>The prerendered exercise and its lessons, handed to the interactive render.</summary>
    private sealed record DetailSnapshot(ExerciseDetailDto? Exercise, List<LessonUsingExerciseDto> Lessons);
}

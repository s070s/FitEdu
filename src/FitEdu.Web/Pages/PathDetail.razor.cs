using Microsoft.AspNetCore.Components;
using FitEdu.Application.Common;
using FitEdu.Application.DTOs;
using FitEdu.Shared.Resources;
using FitEdu.Web.Consumers;
using System.Globalization;

namespace FitEdu.Web.Pages;

public partial class PathDetail
{
    private const int HeroRingSize = 88;
    private const int FullPercent = 100;
    private const string CourseUrlPrefix = "/courses/";

    [Parameter] public Guid Id { get; set; }

    [Inject] private IPathConsumer PathConsumer { get; set; } = default!;

    private LearningPathDetailDto? _path;
    private bool _loading = true;

    private static string CurrentLang =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "el" ? "el" : "en";

    private int StepCount => _path?.Steps.Count ?? 0;

    private int CompletedCount => _path?.Steps.Count(step => step.IsCompleted) ?? 0;

    /// <summary>Completed courses as a whole percentage; a path with no courses is at 0.</summary>
    private int PercentComplete => StepCount == 0 ? 0 : FullPercent * CompletedCount / StepCount;

    private bool IsPathComplete => StepCount > 0 && CompletedCount == StepCount;

    /// <summary>The course the member should open next; null for guests and finished paths.</summary>
    private LearningPathStepDto? NextStep => _path?.Steps.FirstOrDefault(step => step.IsNext);

    private string NextStepLabel => CompletedCount == 0 ? PathRes.StartPath : PathRes.ContinuePath;

    /// <summary>The ring's centre shows "completed/total": a number pair, the same in every language.</summary>
    private string RingLabel => $"{CompletedCount}/{StepCount}";

    /// <summary>"1 of 3 completed": the hero's progress label and the ring's accessible name.</summary>
    private string ProgressText =>
        ResourceFormat.Fill(PathRes.ProgressFormat, CompletedCount, StepCount);

    private string StepsCountText =>
        ResourceFormat.Count(StepCount, PathRes.StepCountOne, PathRes.StepsCountFormat);

    protected override async Task OnParametersSetAsync()
    {
        _loading = true;
        _path = await PathConsumer.GetPathAsync(Id, CurrentLang);
        _loading = false;
    }

    private static string CourseUrl(LearningPathStepDto step) => CourseUrlPrefix + step.CourseId;

    private static string StepNumberText(LearningPathStepDto step) =>
        ResourceFormat.Fill(PathRes.StepNumberFormat, step.StepOrder);

    /// <summary>
    /// A step links to its course unless it is locked and not yet done. Courses are shared between
    /// paths, so a course finished through another path stays open even while it is locked here.
    /// </summary>
    private static bool IsStepOpen(LearningPathStepDto step) => step.IsCompleted || !step.IsLocked;

    /// <summary>
    /// The state classes of one step. They are independent: a course finished out of order can be
    /// both completed and locked, and the marker and badge then show "completed".
    /// </summary>
    private static string StepStateClasses(LearningPathStepDto step)
    {
        var classes = new List<string>();
        if (step.IsCompleted)
            classes.Add("step-completed");
        if (step.IsLocked)
            classes.Add("step-locked");
        if (step.IsNext)
            classes.Add("step-next");
        return string.Join(' ', classes);
    }
}

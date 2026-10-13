using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using FitEdu.Web.Consumers;
using FitEdu.Application.DTOs;
using FitEdu.Shared.Resources;
using System.Globalization;
using System.Text.Json;

namespace FitEdu.Web.Pages;

/// <summary>
/// Student form for one self-check. Every section is rendered; on phones CSS shows one section
/// at a time and the sticky stepper moves between them, while wider screens show them all.
/// </summary>
public partial class AssessmentForm
{
    private const int FullPercent = 100;
    private const string MultiSelectSeparator = ",";

    [Parameter]
    public Guid AssessmentId
    {
        get; set;
    }

    [Inject] private IAssessmentConsumer AssessmentConsumer { get; set; } = default!;
    [Inject] private ISubscriptionConsumer SubscriptionService { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private ILogger<AssessmentForm> _logger { get; set; } = default!;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private AssessmentDefinitionDto? assessment;
    private List<QuestionSchema>? questions;
    private List<FormSection> _sections = [];
    private ElementReference[] _sectionHeadings = [];
    private int _currentSection;
    private bool _focusSectionAfterRender;
    private Dictionary<string, string> answers = new();

    /// <summary>Ticked options per multi-select question, in the order they were ticked.</summary>
    private readonly Dictionary<string, List<string>> _multiSelections = new();

    private bool isSubmitting = false;
    private bool submitted = false;
    private bool _loading = true;
    private string? _loadError;
    private string? _submitError;
    private HashSet<string> _missingRequiredIds = new();

    private int QuestionCount => questions?.Count ?? 0;

    private int AnsweredCount => (questions ?? []).Count(question => IsAnswered(question.Id));

    private int AnsweredPercent => QuestionCount == 0 ? 0 : FullPercent * AnsweredCount / QuestionCount;

    private string AnsweredRingText => $"{AnsweredCount}/{QuestionCount}";

    /// <summary>More than one section: the form gets section headings and, on phones, steps.</summary>
    private bool HasSteps => _sections.Count > 1;

    private bool IsLastSection => _currentSection >= _sections.Count - 1;

    protected override async Task OnInitializedAsync()
    {
        var status = await SubscriptionService.GetStatusAsync();
        if (status?.Features?.AssessmentAccess != true)
        {
            Nav.NavigateTo("/pricing");
            return;
        }

        try
        {
            var lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "el" ? "el" : "en";
            assessment = await AssessmentConsumer.GetAssessmentAsync(AssessmentId, lang);
            if (assessment is null)
            {
                _loadError = AssessmentRes.AssessmentNotFound;
                return;
            }

            LoadSchema(assessment.SchemaJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading assessment {AssessmentId}.", AssessmentId);
            _loadError = AssessmentRes.FailedToLoadAssessment;
        }
        finally
        {
            _loading = false;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_focusSectionAfterRender)
            return;

        _focusSectionAfterRender = false;
        await FocusCurrentSectionAsync();
    }

    private void LoadSchema(string schemaJson)
    {
        var schema = JsonSerializer.Deserialize<AssessmentSchema>(schemaJson, JsonOptions);
        questions = schema?.Questions ?? [];
        _sections = BuildSections(questions, schema?.Sections);
        _sectionHeadings = new ElementReference[_sections.Count];

        foreach (var question in questions)
            answers[question.Id] = string.Empty;
    }

    /// <summary>
    /// Groups the flat question list by the schema's section outline. A schema without sections
    /// becomes one untitled section; a question no section lists goes into a trailing untitled section.
    /// </summary>
    private static List<FormSection> BuildSections(List<QuestionSchema> allQuestions, List<SectionSchema>? outline)
    {
        if (outline is null || outline.Count == 0)
            return [new FormSection(null, allQuestions)];

        var questionsById = allQuestions
            .GroupBy(question => question.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var placedIds = new HashSet<string>();
        var sections = new List<FormSection>();

        foreach (var section in outline)
        {
            var sectionQuestions = section.QuestionIds
                .Where(id => questionsById.ContainsKey(id) && placedIds.Add(id))
                .Select(id => questionsById[id])
                .ToList();
            if (sectionQuestions.Count > 0)
                sections.Add(new FormSection(NullIfBlank(section.Title), sectionQuestions));
        }

        var unplaced = allQuestions.Where(question => !placedIds.Contains(question.Id)).ToList();
        if (unplaced.Count > 0)
            sections.Add(new FormSection(null, unplaced));

        return sections;
    }

    private static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    private bool IsAnswered(string questionId) => !string.IsNullOrWhiteSpace(answers.GetValueOrDefault(questionId));

    private bool IsOptionSelected(string questionId, string option) =>
        _multiSelections.TryGetValue(questionId, out var selected) && selected.Contains(option);

    /// <summary>The rating's end label for its lowest or highest value, or null for the values between.</summary>
    private static string? RatingEndLabel(QuestionSchema question, int ratingValue)
    {
        if (ratingValue == question.Min)
            return NullIfBlank(question.MinLabel);

        return ratingValue == question.Max ? NullIfBlank(question.MaxLabel) : null;
    }

    private void SetAnswer(string questionId, string? value)
    {
        answers[questionId] = value ?? string.Empty;
        if (IsAnswered(questionId))
            _missingRequiredIds.Remove(questionId);
    }

    private void GoBack() => Nav.NavigateTo("/assessments");

    /// <summary>
    /// Keeps the ticked options in a list, so an option that itself contains a comma is still
    /// added and removed whole; the stored answer stays the comma-joined text.
    /// </summary>
    private void ToggleMultiSelect(string questionId, string option, bool isChecked)
    {
        if (!_multiSelections.TryGetValue(questionId, out var selected))
        {
            selected = [];
            _multiSelections[questionId] = selected;
        }

        if (isChecked && !selected.Contains(option))
            selected.Add(option);
        else if (!isChecked)
            selected.Remove(option);

        SetAnswer(questionId, string.Join(MultiSelectSeparator, selected));
    }

    private HashSet<string> FindMissingRequired(IEnumerable<QuestionSchema> questionsToCheck) =>
        questionsToCheck
            .Where(question => question.Required && !IsAnswered(question.Id))
            .Select(question => question.Id)
            .ToHashSet();

    /// <summary>Moves to the next section once every required question of this one is answered.</summary>
    private void GoToNextSection()
    {
        if (IsLastSection)
            return;

        _missingRequiredIds = FindMissingRequired(_sections[_currentSection].Questions);
        if (_missingRequiredIds.Count > 0)
        {
            _submitError = AssessmentRes.RequiredQuestionMissing;
            return;
        }

        _submitError = null;
        ShowSection(_currentSection + 1);
    }

    private void GoToPreviousSection()
    {
        if (_currentSection == 0)
            return;

        _submitError = null;
        ShowSection(_currentSection - 1);
    }

    private void ShowSection(int sectionIndex)
    {
        _currentSection = Math.Clamp(sectionIndex, 0, Math.Max(0, _sections.Count - 1));
        _focusSectionAfterRender = HasSteps;
    }

    /// <summary>
    /// Moves focus to the new section's heading, which also scrolls it into view under the sticky
    /// header. Focus is a convenience: a lost circuit must not break the form.
    /// </summary>
    private async Task FocusCurrentSectionAsync()
    {
        if (_currentSection >= _sectionHeadings.Length)
            return;

        try
        {
            await _sectionHeadings[_currentSection].FocusAsync();
        }
        catch (JSDisconnectedException)
        {
            // The browser went away; nothing to focus.
        }
        catch (InvalidOperationException)
        {
            // The heading was not rendered (yet); the section is still shown, just not focused.
        }
    }

    private int FirstSectionWithMissingAnswer() =>
        _sections.FindIndex(section => section.Questions.Any(question => _missingRequiredIds.Contains(question.Id)));

    private async Task HandleSubmit()
    {
        _submitError = null;
        _missingRequiredIds = FindMissingRequired(questions ?? []);

        if (_missingRequiredIds.Count > 0)
        {
            _submitError = AssessmentRes.RequiredQuestionMissing;
            ShowSection(Math.Max(0, FirstSectionWithMissingAnswer()));
            return;
        }

        isSubmitting = true;
        try
        {
            var answersJson = JsonSerializer.Serialize(answers);
            var request = new SubmitAssessmentRequest(answersJson, null);

            var submission = await AssessmentConsumer.SubmitAssessmentAsync(AssessmentId, request);

            if (submission is not null)
            {
                submitted = true;
            }
            else
            {
                _logger.LogError("Assessment submission {AssessmentId} returned null.", AssessmentId);
                _submitError = AssessmentRes.SubmitFailed;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error submitting assessment {AssessmentId}.", AssessmentId);
            _submitError = AssessmentRes.SubmitFailed;
        }
        finally
        {
            isSubmitting = false;
        }
    }

    private void ViewHistory()
    {
        Nav.NavigateTo("/assessments/mine");
    }

    /// <summary>One step of the form: an optional title and its questions.</summary>
    private sealed record FormSection(string? Title, List<QuestionSchema> Questions);

    private sealed class AssessmentSchema
    {
        public List<QuestionSchema> Questions { get; set; } = new();

        /// <summary>Section outline written by the API: id, title and the ids of its questions.</summary>
        public List<SectionSchema>? Sections
        {
            get; set;
        }
    }

    private sealed class SectionSchema
    {
        public string? Title
        {
            get; set;
        }
        public List<string> QuestionIds { get; set; } = new();
    }

    private sealed class QuestionSchema
    {
        public string Id { get; set; } = string.Empty;
        public string? Text
        {
            get; set;
        }
        public string? Label
        {
            get; set;
        }
        public string Type { get; set; } = string.Empty;
        public List<string>? Options
        {
            get; set;
        }
        public int Min { get; set; } = 1;
        public int Max { get; set; } = 5;
        public string? MinLabel
        {
            get; set;
        }
        public string? MaxLabel
        {
            get; set;
        }
        public bool Required
        {
            get; set;
        }

        /// <summary>Uses Label if present, falls back to Text.</summary>
        public string DisplayLabel => Label ?? Text ?? string.Empty;
    }
}

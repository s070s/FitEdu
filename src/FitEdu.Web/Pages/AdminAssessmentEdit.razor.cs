using Microsoft.AspNetCore.Components;
using FitEdu.Shared.Resources;
using FitEdu.Shared.Resources.Messages;
using Microsoft.AspNetCore.Components.Web;
using FitEdu.Web.Consumers;
using FitEdu.Web.Shared;
using FitEdu.Web.Shared.Components.Forms;
using FitEdu.Application.DTOs;

namespace FitEdu.Web.Pages;

public partial class AdminAssessmentEdit
{
    [Parameter]
    public Guid AssessmentId
    {
        get; set;
    }

    [Inject] private IAdminAssessmentConsumer AssessmentConsumer { get; set; } = default!;
    [Inject] private IAdminCategoryConsumer CategoryConsumer { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    private bool IsNew => AssessmentId == Guid.Empty;
    private bool loading = true;
    private bool isSaving;
    private string message = string.Empty;
    private string messageType = "danger";
    private bool _isDirty;

    private void MarkDirty() => _isDirty = true;

    private string assessmentKey = string.Empty;
    private string assessmentTitleEn = string.Empty;
    private string? assessmentTitleEl;
    private string? assessmentDescriptionEn;
    private string? assessmentDescriptionEl;
    private QuillEditor? descriptionEditorEn;
    private QuillEditor? descriptionEditorEl;
    private EditableAssessmentSchema schema = new();
    private HashSet<string> _expandedQuestions = new();

    // Category picker state
    private List<CategoryOptionDto> categories = [];
    private Guid? assessmentCategoryId;
    private bool creatingNewCategory;
    private string? newCategoryName;

    protected override async Task OnInitializedAsync()
    {
        await LoadCategories();

        if (!IsNew)
        {
            await LoadAssessment();
        }

        // A new (or empty) assessment starts with one section, so "Add Question" is there to click.
        if (schema.Sections.Count == 0)
            schema.Sections.Add(new EditableSection { Id = schema.NextSectionId() });

        loading = false;
        _isDirty = false;
    }

    private async Task LoadCategories()
    {
        try
        {
            categories = await CategoryConsumer.GetAllCategoriesAsync();
        }
        catch
        {
            categories = [];
        }
    }

    private async Task LoadAssessment()
    {
        try
        {
            var assessment = await AssessmentConsumer.GetAssessmentAsync(AssessmentId);
            if (assessment != null)
            {
                assessmentKey = assessment.Key;
                assessmentTitleEn = assessment.TitleEn;
                assessmentTitleEl = assessment.TitleEl;
                assessmentDescriptionEn = assessment.DescriptionEn;
                assessmentDescriptionEl = assessment.DescriptionEl;
                assessmentCategoryId = assessment.CategoryId;
                creatingNewCategory = false;
                newCategoryName = null;
                schema = AssessmentSchemaMapper.Parse(assessment.SchemaJson);
            }
        }
        catch (Exception)
        {
            message = ErrorMessagesRes.UnexpectedErrorTryAgain;
        }
    }

    // ── Category picker ──

    private string CategorySelectValue => creatingNewCategory ? "new" : (assessmentCategoryId?.ToString() ?? "none");

    private void OnCategorySelectChanged(ChangeEventArgs e)
    {
        var value = e.Value?.ToString();
        if (value == "new")
        {
            creatingNewCategory = true;
            assessmentCategoryId = null;
        }
        else if (string.IsNullOrEmpty(value) || value == "none")
        {
            creatingNewCategory = false;
            assessmentCategoryId = null;
            newCategoryName = null;
        }
        else
        {
            creatingNewCategory = false;
            newCategoryName = null;
            assessmentCategoryId = Guid.Parse(value);
        }
    }

    // ── Sections and questions ──

    private void AddSection()
    {
        schema.Sections.Add(new EditableSection { Id = schema.NextSectionId() });
        _isDirty = true;
    }

    private void RemoveSection(EditableSection section)
    {
        foreach (var question in section.Questions)
            _expandedQuestions.Remove(question.TempId);

        schema.Sections.Remove(section);
        _isDirty = true;
    }

    private void AddQuestion(EditableSection section)
    {
        var question = new EditableQuestion { Id = schema.NextQuestionId() };
        section.Questions.Add(question);
        _expandedQuestions.Add(question.TempId);
        _isDirty = true;
    }

    private void RemoveQuestion(EditableSection section, int index)
    {
        if (index < 0 || index >= section.Questions.Count)
            return;

        _expandedQuestions.Remove(section.Questions[index].TempId);
        section.Questions.RemoveAt(index);
        _isDirty = true;
    }

    private void ToggleQuestion(string tempId)
    {
        if (!_expandedQuestions.Remove(tempId))
            _expandedQuestions.Add(tempId);
    }

    private void MoveQuestion(EditableSection section, int index, int direction)
    {
        var questions = section.Questions;
        var newIndex = index + direction;
        if (newIndex < 0 || newIndex >= questions.Count)
            return;

        (questions[index], questions[newIndex]) = (questions[newIndex], questions[index]);
        _isDirty = true;
    }

    /// <summary>
    /// The type drop-down: the editor's four types, plus the question's own type when it is another
    /// one (e.g. "multi-select"), so opening and saving never changes a question's type.
    /// </summary>
    private static List<TypeOption> TypeOptions(string currentType)
    {
        var options = new List<TypeOption>
        {
            new(AssessmentQuestionTypes.Text, AdminRes.TypeText),
            new(AssessmentQuestionTypes.Rating, AdminRes.TypeRating),
            new(AssessmentQuestionTypes.Choice, AdminRes.TypeChoice),
            new(AssessmentQuestionTypes.MultiSelect, AdminRes.TypeMultiSelect)
        };

        if (!options.Exists(option => option.Value == currentType))
            options.Add(new TypeOption(currentType, currentType));

        return options;
    }

    private sealed record TypeOption(string Value, string Label);

    private async Task SaveAssessment()
    {
        isSaving = true;
        message = string.Empty;
        try
        {
            var descEn = descriptionEditorEn != null
                ? await descriptionEditorEn.GetContentAsync()
                : assessmentDescriptionEn;

            var descEl = descriptionEditorEl != null
                ? await descriptionEditorEl.GetContentAsync()
                : assessmentDescriptionEl;

            var schemaJson = AssessmentSchemaMapper.Serialize(schema, assessmentKey, assessmentTitleEn, assessmentTitleEl);

            var categoryIdArg = creatingNewCategory ? null : assessmentCategoryId;
            var newCategoryNameArg = creatingNewCategory ? newCategoryName : null;

            var request = new SaveAssessmentDefinitionRequest(
                assessmentKey,
                assessmentTitleEn,
                assessmentTitleEl,
                descEn,
                descEl,
                schemaJson,
                categoryIdArg,
                newCategoryNameArg
            );

            AdminAssessmentDefinitionDto? result;
            if (IsNew)
                result = await AssessmentConsumer.CreateAssessmentAsync(request);
            else
                result = await AssessmentConsumer.UpdateAssessmentAsync(AssessmentId, request);

            if (result is not null)
            {
                _isDirty = false;
                Nav.NavigateTo("/admin/assessments");
            }
            else
                message = AdminRes.AssessmentSaveFailed;
        }
        catch (Exception)
        {
            message = ErrorMessagesRes.UnexpectedErrorTryAgain;
        }
        finally
        {
            isSaving = false;
        }
    }

    private void GoBack()
    {
        Nav.NavigateTo("/admin/assessments");
    }
}

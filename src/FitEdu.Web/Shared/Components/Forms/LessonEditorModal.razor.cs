using Microsoft.AspNetCore.Components;
using FitEdu.Shared.Resources;
using FitEdu.Shared.Resources.Messages;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using FitEdu.Web.Consumers;
using FitEdu.Web.Extensions;
using FitEdu.Application.DTOs;

namespace FitEdu.Web.Shared.Components.Forms;

public partial class LessonEditorModal
{
    [Parameter, EditorRequired] public bool IsVisible { get; set; }
    [Parameter] public AdminLessonDto? EditingLesson { get; set; }
    [Parameter, EditorRequired] public Guid ModuleId { get; set; }
    [Parameter] public int DefaultSortOrder { get; set; } = 1;
    [Parameter, EditorRequired] public EventCallback OnClose { get; set; }
    [Parameter, EditorRequired] public EventCallback OnSaved { get; set; }

    [Inject] private IAdminLessonConsumer LessonConsumer { get; set; } = default!;
    [Inject] private IExerciseConsumer ExerciseConsumer { get; set; } = default!;

    // The picker shows one short page of matches; the admin narrows the search instead of paging.
    private const int ExerciseSearchPageSize = 10;

    private string _titleEn = string.Empty;
    private string? _titleEl;
    private string? _contentEn;
    private string? _contentEl;
    private string? _videoUrl;
    private int _sortOrder;
    private int? _duration;
    private string? _existingPdfPath;
    private string? _existingVideoPath;
    private IBrowserFile? _pendingPdf;
    private IBrowserFile? _pendingVideo;
    private QuillEditor? _contentEditorEn;
    private QuillEditor? _contentEditorEl;
    private bool _isSaving;
    private string _errorMessage = string.Empty;
    private bool _wasVisible;

    // Exercise rows in lesson order, and the picker's search state (null results = no search yet)
    private List<ExerciseRow> _exerciseRows = [];
    private string _exerciseSearch = string.Empty;
    private List<ExerciseListItemDto>? _exerciseSearchResults;
    private bool _isSearching;

    // The API refuses longer lists (SaveLessonRequest.Exercises), so the picker stops adding here.
    private bool IsExerciseListFull => _exerciseRows.Count >= SaveLessonRequest.MaxExercises;

    protected override void OnParametersSet()
    {
        var justOpened = IsVisible && !_wasVisible;
        _wasVisible = IsVisible;

        if (!justOpened)
            return;

        _errorMessage = string.Empty;
        _pendingPdf = null;
        _pendingVideo = null;
        _exerciseSearch = string.Empty;
        _exerciseSearchResults = null;
        _exerciseRows = EditingLesson?.Exercises?.Select(ExerciseRow.FromDto).ToList() ?? [];

        if (EditingLesson is not null)
        {
            _titleEn = EditingLesson.TitleEn;
            _titleEl = EditingLesson.TitleEl;
            _contentEn = LessonMarkdown.RenderForEditor(EditingLesson.ContentEn, EditingLesson.TitleEn);
            _contentEl = LessonMarkdown.RenderForEditor(EditingLesson.ContentEl, EditingLesson.TitleEl);
            _videoUrl = EditingLesson.VideoPath;
            _sortOrder = EditingLesson.SortOrder;
            _duration = EditingLesson.DurationMinutes;
            _existingPdfPath = EditingLesson.PdfPath;
            _existingVideoPath = EditingLesson.VideoPath;
        }
        else
        {
            _titleEn = string.Empty;
            _titleEl = null;
            _contentEn = null;
            _contentEl = null;
            _videoUrl = null;
            _sortOrder = DefaultSortOrder;
            _duration = null;
            _existingPdfPath = null;
            _existingVideoPath = null;
        }
    }

    private void OnPdfSelected(InputFileChangeEventArgs e) => _pendingPdf = e.File;
    private void OnVideoSelected(InputFileChangeEventArgs e) => _pendingVideo = e.File;

    // ── Exercise picker ──

    private async Task SearchExercisesAsync()
    {
        // The Enter key skips the disabled search button, so a running search is checked here too.
        if (_isSearching)
            return;

        var searchTerm = _exerciseSearch.Trim();
        if (searchTerm.Length == 0)
        {
            _exerciseSearchResults = null;
            return;
        }

        _isSearching = true;
        _errorMessage = string.Empty;
        try
        {
            var filter = new ExerciseFilterDto(Search: searchTerm);
            var page = await ExerciseConsumer.ListAsync(1, ExerciseSearchPageSize, filter: filter);
            _exerciseSearchResults = page.Items;
        }
        catch (Exception)
        {
            // The consumer already turns network failures into an empty page; anything else
            // (such as a response body that is not valid JSON) would otherwise break the circuit.
            _exerciseSearchResults = null;
            _errorMessage = ErrorMessagesRes.UnexpectedErrorTryAgain;
        }
        finally
        {
            _isSearching = false;
        }
    }

    private async Task OnExerciseSearchKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
            await SearchExercisesAsync();
    }

    private void AddExercise(ExerciseListItemDto exercise)
    {
        if (IsExerciseListFull)
            return;

        _exerciseRows.Add(new ExerciseRow { Slug = exercise.Slug, Name = exercise.Name });
    }

    private void RemoveExercise(int index) => _exerciseRows.RemoveAt(index);

    private void MoveExerciseUp(int index) => SwapExercises(index, index - 1);

    private void MoveExerciseDown(int index) => SwapExercises(index, index + 1);

    private void SwapExercises(int index, int otherIndex)
    {
        if (otherIndex < 0 || otherIndex >= _exerciseRows.Count)
            return;

        (_exerciseRows[index], _exerciseRows[otherIndex]) = (_exerciseRows[otherIndex], _exerciseRows[index]);
    }

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(_titleEn))
        {
            _errorMessage = AdminRes.LessonTitleRequired;
            return;
        }

        _errorMessage = string.Empty;
        _isSaving = true;
        try
        {
            var contentEn = _contentEditorEn != null
                ? await _contentEditorEn.GetContentAsync()
                : _contentEn;

            var contentEl = _contentEditorEl != null
                ? await _contentEditorEl.GetContentAsync()
                : _contentEl;

            // The list is always sent, so saving replaces the lesson's exercises with exactly these rows.
            var exercises = _exerciseRows.Select(row => row.ToDto()).ToList();
            var request = new SaveLessonRequest(
                _titleEn, _titleEl, contentEn, contentEl, _videoUrl, _duration, _sortOrder, ModuleId, exercises);

            Guid lessonId;

            if (EditingLesson is null)
            {
                var created = await LessonConsumer.CreateLessonAsync(request);
                if (created is null)
                {
                    _errorMessage = AdminRes.LessonCreateFailed;
                    return;
                }
                lessonId = created.Id;
            }
            else
            {
                lessonId = EditingLesson.Id;
                var updated = await LessonConsumer.UpdateLessonAsync(lessonId, request);
                if (updated is null)
                {
                    _errorMessage = AdminRes.LessonUpdateFailed;
                    return;
                }
            }

            var uploadErrors = new System.Text.StringBuilder();

            if (_pendingPdf != null)
            {
                var result = await LessonConsumer.UploadPdfAsync(lessonId, _pendingPdf);
                if (result is null) uploadErrors.AppendLine(AdminRes.PdfUploadFailed);
            }

            if (_pendingVideo != null)
            {
                var result = await LessonConsumer.UploadVideoAsync(lessonId, _pendingVideo);
                if (result is null) uploadErrors.AppendLine(AdminRes.VideoUploadFailed);
            }

            if (uploadErrors.Length > 0)
            {
                _errorMessage = uploadErrors.ToString().Trim();
                return;
            }

            await OnSaved.InvokeAsync();
        }
        catch (Exception ex)
        {
            _errorMessage = ErrorMessagesRes.UnexpectedErrorTryAgain;
        }
        finally
        {
            _isSaving = false;
        }
    }

    /// <summary>Editable copy of one exercise row (the DTO is an immutable record).</summary>
    private sealed class ExerciseRow
    {
        public required string Slug { get; init; }
        public required string Name { get; init; }
        public int? Sets { get; set; }
        public string? Reps { get; set; }
        public int? RestSeconds { get; set; }
        public string? NotesEn { get; set; }
        public string? NotesEl { get; set; }

        public static ExerciseRow FromDto(LessonExerciseEditDto dto) => new()
        {
            Slug = dto.Slug,
            Name = dto.Name ?? dto.Slug,
            Sets = dto.Sets,
            Reps = dto.Reps,
            RestSeconds = dto.RestSeconds,
            NotesEn = dto.NotesEn,
            NotesEl = dto.NotesEl
        };

        public LessonExerciseEditDto ToDto() => new(Slug, Sets, Reps, RestSeconds, NotesEn, NotesEl, Name);
    }
}

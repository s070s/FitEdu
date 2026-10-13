using Microsoft.AspNetCore.Components;
using FitEdu.Application.Common;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Entities;
using FitEdu.Shared.Resources;
using FitEdu.Web.Consumers;

namespace FitEdu.Web.Pages;

public partial class AdminSessions
{
    [Inject] private IAdminSessionConsumer SessionConsumer { get; set; } = default!;
    [Inject] private IAdminCourseConsumer CourseConsumer { get; set; } = default!;

    private PagedResult<AdminScheduledSessionDto>? pagedResult;
    private int currentPage = 1;
    private int pageSize = 10;
    private static readonly int[] PageSizeOptions = [10, 25, 50];
    private string _sortBy = "startsatutc";
    private string _sortDir = "asc";
    private string message = string.Empty;

    private List<AdminCourseDto> _courses = [];

    private bool _formModalVisible;
    private Guid? _editingId;
    private string _formTitleEn = string.Empty;
    private string? _formTitleEl;
    private Guid? _formCourseId;
    // The start time as typed in the form: wall-clock time in the members' time zone (SiteClock).
    private DateTime _formStartsAtLocal = DefaultStartsAtLocal();
    private int _formDurationMinutes = 30;
    private int _formMaxParticipants = ScheduledSession.MaxParticipantsLimit;
    private bool _isSaving;
    private string? _formError;

    private AdminScheduledSessionDto? _pendingCancel;

    protected override async Task OnInitializedAsync()
    {
        await LoadCoursesAsync();
        await LoadAsync();
    }

    private async Task LoadCoursesAsync()
    {
        try { _courses = (await CourseConsumer.GetCoursesAsync(1, 200, "titleen", "asc"))?.Items.ToList() ?? []; }
        catch { _courses = []; }
    }

    private async Task LoadAsync()
    {
        pagedResult = await SessionConsumer.GetAllAsync(currentPage, pageSize, _sortBy, _sortDir);
    }

    private async Task OnSort(string columnKey)
    {
        if (_sortBy == columnKey)
            _sortDir = _sortDir == "asc" ? "desc" : "asc";
        else
        {
            _sortBy = columnKey;
            _sortDir = "asc";
        }
        currentPage = 1;
        await LoadAsync();
    }

    private async Task OnPageSizeChanged(int size)
    {
        pageSize = size;
        currentPage = 1;
        await LoadAsync();
    }

    private async Task PreviousPage()
    {
        if (currentPage > 1)
        {
            currentPage--;
            await LoadAsync();
        }
    }

    private async Task NextPage()
    {
        if (pagedResult is { HasNextPage: true })
        {
            currentPage++;
            await LoadAsync();
        }
    }

    private void ShowAddModal()
    {
        _editingId = null;
        _formTitleEn = string.Empty;
        _formTitleEl = null;
        _formCourseId = null;
        _formStartsAtLocal = DefaultStartsAtLocal();
        _formDurationMinutes = 30;
        _formMaxParticipants = ScheduledSession.MaxParticipantsLimit;
        _formError = null;
        _formModalVisible = true;
    }

    private void ShowEditModal(AdminScheduledSessionDto session)
    {
        _editingId = session.Id;
        _formTitleEn = session.TitleEn;
        _formTitleEl = session.TitleEl;
        _formCourseId = session.CourseId;
        _formStartsAtLocal = SiteClock.ToSiteTime(session.StartsAtUtc).DateTime;
        _formDurationMinutes = session.DurationMinutes;
        _formMaxParticipants = session.MaxParticipants;
        _formError = null;
        _formModalVisible = true;
    }

    private void CloseFormModal() => _formModalVisible = false;

    /// <summary>A new class starts an hour from now by default, in the members' time zone.</summary>
    private static DateTime DefaultStartsAtLocal() => SiteClock.ToSiteTime(DateTimeOffset.UtcNow).DateTime.AddHours(1);

    private void OnCourseChanged(ChangeEventArgs e)
    {
        var value = e.Value?.ToString();
        _formCourseId = string.IsNullOrEmpty(value) ? null : Guid.Parse(value);
    }

    private async Task SubmitForm()
    {
        _isSaving = true;
        _formError = null;
        try
        {
            // Back to UTC with the same zone the form was filled in.
            var startsAtUtc = SiteClock.FromSiteTime(_formStartsAtLocal);
            var request = new SaveScheduledSessionRequest(
                _formTitleEn.Trim(),
                string.IsNullOrWhiteSpace(_formTitleEl) ? null : _formTitleEl.Trim(),
                _formCourseId,
                startsAtUtc,
                _formDurationMinutes,
                _formMaxParticipants);

            var result = _editingId is { } id
                ? await SessionConsumer.UpdateAsync(id, request)
                : await SessionConsumer.CreateAsync(request);

            if (result is not null)
            {
                _formModalVisible = false;
                await LoadAsync();
                message = SessionRes.Save;
            }
            else
            {
                _formError = SessionRes.SaveError;
            }
        }
        finally
        {
            _isSaving = false;
        }
    }

    private void RequestCancel(AdminScheduledSessionDto session) => _pendingCancel = session;

    private async Task ConfirmCancel()
    {
        if (_pendingCancel is null)
            return;

        var id = _pendingCancel.Id;
        _pendingCancel = null;

        if (await SessionConsumer.CancelAsync(id))
        {
            message = SessionRes.CancelSession;
            await LoadAsync();
        }
    }
}

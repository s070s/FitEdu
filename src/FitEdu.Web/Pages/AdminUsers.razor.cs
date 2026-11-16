using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using FitEdu.Shared.Resources.Messages;
using FitEdu.Web.Consumers;
using FitEdu.Web.Interfaces;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.Common;
using FitEdu.Application.DTOs;
using FitEdu.Shared.Resources;

namespace FitEdu.Web.Pages;

public partial class AdminUsers : IAsyncDisposable
{
    private const string SnapshotPersistKey = "admin-users-snapshot";

    [Inject] private IAdminUserConsumer UserConsumer { get; set; } = default!;
    [Inject] private IAuthService AuthService { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthState { get; set; } = default!;
    [Inject] private PersistentComponentState ApplicationState { get; set; } = default!;

    /// <summary>
    /// What the prerender hands to the interactive render (and a resumed circuit): the loaded list together
    /// with the paging, search and sort it was loaded for, so the page never shows one list with another's state.
    /// </summary>
    public sealed record UsersSnapshot(
        int Page, int PageSize, string Search, string SortBy, string SortDir, PagedResult<AdminUserDto> Result);

    private PagedResult<AdminUserDto>? pagedResult;
    private int currentPage = 1;
    private int pageSize = 10;
    private static readonly int[] PageSizeOptions = [10, 25, 50, 100];
    private string searchTerm = string.Empty;
    private string message = string.Empty;
    private string messageType = "success";
    private string _sortBy = "email";
    private string _sortDir = "asc";
    private AdminUserDto? _deleteTarget;
    private CancellationTokenSource? _searchCts;
    private bool _loadFailed;
    private int _loadSequence;
    private PersistingComponentStateSubscription _persistSubscription;
    private ElementReference _headingRef;
    private bool _focusHeadingAfterRender;

    private bool _resetPwdModalVisible;
    private string? _resetPwdUserId;
    private string _resetPwdEmail = string.Empty;
    private string _resetPwdNew = string.Empty;
    private string _resetPwdConfirm = string.Empty;
    private bool _resetPwdBusy;
    private string? _resetPwdError;

    protected override async Task OnInitializedAsync()
    {
        _persistSubscription = ApplicationState.RegisterOnPersisting(PersistSnapshot);

        // The prerender already loaded the list; the interactive render reuses it, with the paging it
        // was loaded for, instead of asking the API a second time.
        if (ApplicationState.TryTakeFromJson<UsersSnapshot>(SnapshotPersistKey, out var snapshot) && snapshot is not null)
        {
            currentPage = snapshot.Page;
            pageSize = snapshot.PageSize;
            searchTerm = snapshot.Search;
            _sortBy = snapshot.SortBy;
            _sortDir = snapshot.SortDir;
            pagedResult = snapshot.Result;
            return;
        }

        await LoadUsers();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // The deleted user's row button had the focus; with the row gone the dialog cannot give it back,
        // so it goes to a fixed place instead of falling to the top of the document.
        if (!_focusHeadingAfterRender)
            return;

        _focusHeadingAfterRender = false;
        await _headingRef.FocusAsync();
    }

    private Task PersistSnapshot()
    {
        // A failed load stays out, so the interactive render asks the API again.
        if (pagedResult is not null)
            ApplicationState.PersistAsJson(SnapshotPersistKey, new UsersSnapshot(currentPage, pageSize, searchTerm, _sortBy, _sortDir, pagedResult));

        return Task.CompletedTask;
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
        await LoadUsers();
    }

    private async Task LoadUsers()
    {
        // Answers can arrive out of order (a second search typed while the first is still loading):
        // only the newest request may change the page.
        var sequence = ++_loadSequence;
        _loadFailed = false;
        try
        {
            var loaded = await UserConsumer.GetUsersAsync(
                currentPage,
                pageSize,
                string.IsNullOrEmpty(searchTerm) ? null : searchTerm,
                _sortBy,
                _sortDir);
            if (sequence != _loadSequence)
                return;

            // The consumer answers null for any failed request (an error status, no connection).
            if (loaded is null)
            {
                message = ErrorMessagesRes.UnexpectedErrorTryAgain;
                messageType = "danger";
                _loadFailed = true;
                return;
            }

            pagedResult = loaded;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            if (sequence != _loadSequence)
                return;

            message = ErrorMessagesRes.AccessDenied;
            messageType = "danger";
            _loadFailed = true;
        }
        catch (Exception)
        {
            if (sequence != _loadSequence)
                return;

            // UX-6: any other failure (network, 500, etc.) used to be unhandled here and crash
            // the circuit; pagedResult stays null so the skeleton would otherwise spin forever.
            message = ErrorMessagesRes.UnexpectedErrorTryAgain;
            messageType = "danger";
            _loadFailed = true;
        }
    }

    private async Task OnPageSizeChanged(int size)
    {
        pageSize = size;
        currentPage = 1;
        await LoadUsers();
    }

    private async Task OnSearchInput(ChangeEventArgs e)
    {
        searchTerm = e.Value?.ToString() ?? string.Empty;
        currentPage = 1;

        var previous = _searchCts;
        _searchCts = new CancellationTokenSource();
        previous?.Cancel();
        previous?.Dispose();

        try
        {
            await Task.Delay(300, _searchCts.Token);
            await LoadUsers();
        }
        catch (OperationCanceledException) { }
    }

    private async Task GoToPage(int page)
    {
        currentPage = page;
        await LoadUsers();
    }

    private async Task PreviousPage()
    {
        if (currentPage > 1)
        {
            currentPage--;
            await LoadUsers();
        }
    }

    private async Task NextPage()
    {
        if (pagedResult is { HasNextPage: true })
        {
            currentPage++;
            await LoadUsers();
        }
    }

    private async Task ImpersonateUser(string userId)
    {
        // The circuit has no HttpContext, so the admin is read from the page's authentication state.
        var admin = (await AuthState.GetAuthenticationStateAsync()).User;
        var result = await AuthService.ImpersonateAsync(admin, userId);
        if (result.Success)
        {
            Navigation.NavigateTo(
                $"/auth/complete?ticket={Uri.EscapeDataString(result.Token!)}&returnUrl=%2F",
                forceLoad: true);
        }
        else
        {
            message = result.Message ?? AdminRes.ImpersonationFailed;
            messageType = "danger";
        }
    }

    private void OpenResetPasswordModal(AdminUserDto user)
    {
        _resetPwdUserId = user.Id;
        _resetPwdEmail = user.Email;
        _resetPwdNew = string.Empty;
        _resetPwdConfirm = string.Empty;
        _resetPwdError = null;
        _resetPwdModalVisible = true;
    }

    private void CloseResetPasswordModal()
    {
        _resetPwdModalVisible = false;
        _resetPwdUserId = null;
        _resetPwdNew = string.Empty;
        _resetPwdConfirm = string.Empty;
        _resetPwdError = null;
    }

    private async Task SubmitResetPassword()
    {
        if (string.IsNullOrWhiteSpace(_resetPwdNew) || _resetPwdNew.Length < PasswordRules.MinimumLength)
        {
            _resetPwdError = AdminRes.PasswordMinLength;
            return;
        }

        if (_resetPwdNew != _resetPwdConfirm)
        {
            _resetPwdError = AdminRes.PasswordMismatch;
            return;
        }

        _resetPwdBusy = true;
        _resetPwdError = null;

        try
        {
            var result = await UserConsumer.SetPasswordAsync(_resetPwdUserId!, _resetPwdNew);
            if (result.Succeeded)
            {
                message = AdminRes.PasswordUpdated;
                messageType = "success";
                CloseResetPasswordModal();
            }
            else
            {
                _resetPwdError = PasswordRefusalMessage(result);
            }
        }
        catch (Exception)
        {
            _resetPwdError = ErrorMessagesRes.UnexpectedErrorTryAgain;
        }
        finally
        {
            _resetPwdBusy = false;
        }
    }

    // A 400 carries the server's own rule message (which rule the password broke); any other
    // failure (a missing member, a busy server, no connection) has no rule to show.
    private static string PasswordRefusalMessage(ApiActionResult result) =>
        result.StatusCode == HttpStatusCode.BadRequest && !string.IsNullOrWhiteSpace(result.Detail)
            ? result.Detail
            : AdminRes.PasswordUpdateFailed;

    private async Task SetUserEnabled(string userId, bool enable)
    {
        try
        {
            // API-8: the row already knows the target state (that's how it chose which
            // button to show), so this calls the idempotent enable/disable endpoint
            // directly instead of a toggle that two concurrent admins could race.
            var success = enable
                ? await UserConsumer.EnableUserAsync(userId)
                : await UserConsumer.DisableUserAsync(userId);
            if (success)
            {
                await LoadUsers();
                message = AdminRes.UserStatusToggled;
                messageType = "success";
            }
            else
            {
                message = AdminRes.UserToggleFailed;
                messageType = "danger";
            }
        }
        catch (Exception)
        {
            message = ErrorMessagesRes.UnexpectedErrorTryAgain;
            messageType = "danger";
        }
    }

    private void RequestDeleteUser(AdminUserDto user) => _deleteTarget = user;

    private void CancelDeleteUser() => _deleteTarget = null;

    private async Task DeleteUser()
    {
        if (_deleteTarget is not { } user)
            return;

        _deleteTarget = null;

        try
        {
            var result = await UserConsumer.DeleteUserAsync(user.Id);
            if (result.Succeeded)
            {
                // Deleting the only row of the last page would leave that page empty.
                if (pagedResult?.Items.Count == 1 && currentPage > 1)
                    currentPage--;

                await LoadUsers();
                message = AdminRes.UserDeleted;
                messageType = "success";
                _focusHeadingAfterRender = true;
            }
            else
            {
                message = result.StatusCode == HttpStatusCode.Conflict
                    ? AdminRes.UserDeleteConflict
                    : AdminRes.UserDeleteFailed;
                messageType = "danger";
            }
        }
        catch (Exception)
        {
            message = ErrorMessagesRes.UnexpectedErrorTryAgain;
            messageType = "danger";
        }
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        _persistSubscription.Dispose();
        _searchCts?.Cancel();
        _searchCts?.Dispose();
    }
}


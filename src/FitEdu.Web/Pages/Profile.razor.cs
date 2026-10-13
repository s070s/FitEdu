using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using System.Globalization;
using FitEdu.Web.Consumers;
using FitEdu.Web.Interfaces;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Web.Services;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Enums;
using FitEdu.Shared.Resources;

namespace FitEdu.Web.Pages;

public partial class Profile
{
    [Inject] private IProfileConsumer ProfileConsumer { get; set; } = default!;
    [Inject] private IAuthService AuthService { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthStateProvider { get; set; } = default!;
    [Inject] private AvatarChangedNotifier AvatarNotifier { get; set; } = default!;
    [Inject] private ICourseConsumer CourseConsumer { get; set; } = default!;
    [Inject] private ICertificateConsumer CertificateConsumer { get; set; } = default!;
    [Inject] private IAssessmentConsumer AssessmentConsumer { get; set; } = default!;
    [Inject] private ILogger<Profile> Logger { get; set; } = default!;

    private ProfileDto? profile;
    private bool _isImpersonating;
    private string displayName = string.Empty;
    private UserStatus profileStatus;
    private FitnessGoal? profileGoal;
    private string currentPassword = string.Empty;
    private string newPassword = string.Empty;
    private string confirmPassword = string.Empty;
    private bool isSaving = false;
    private bool isChangingPassword = false;
    private string message = string.Empty;
    private string? avatarDataUrl;
    private string? _loadError;
    private bool _uploadingAvatar;
    private bool _showDeleteConfirm;
    private bool _isDeleting;
    private string? _deleteError;

    // Stat tiles: null hides that tile (its call failed), so one broken endpoint never blanks the rest.
    private int? _enrolledCourses;
    private int? _certificates;
    private int? _assessments;

    private static readonly string[] AllowedAvatarTypes =
        { "image/jpeg", "image/jpg", "image/png", "image/gif", "image/webp" };

    // The catalogue call Home makes too; one page of this size covers every published course.
    private const int CoursePageSize = 100;

    private static string CurrentLang =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "el" ? "el" : "en";

    /// <summary>Only a real training level earns a header chip; "Prefer not to say" would read oddly as a badge.</summary>
    private bool HasLevelChip => profile?.Status is UserStatus.Beginner or UserStatus.Intermediate or UserStatus.Advanced;

    private bool HasAnyStat => _enrolledCourses is not null || _certificates is not null || _assessments is not null;

    /// <summary>The display name when set, else the full name.</summary>
    private string HeroName
    {
        get
        {
            if (profile is null)
                return string.Empty;
            if (!string.IsNullOrWhiteSpace(profile.DisplayName))
                return profile.DisplayName;
            return $"{profile.FirstName} {profile.LastName}".Trim();
        }
    }

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthStateProvider.GetAuthenticationStateAsync();
        _isImpersonating = state.User.FindFirst("impersonatedBy") is not null;
        await LoadProfileAsync();
        if (profile is not null)
            await LoadStatsAsync();
    }

    private async Task LoadProfileAsync()
    {
        _loadError = null;
        try
        {
            profile = await ProfileConsumer.GetProfileAsync();
            if (profile is null)
            {
                // The cookie session is still valid (this page is [Authorize]-gated); a null here
                // means the API call failed, so show a retry instead of silently bouncing to /login.
                _loadError = ProfileRes.FailedToLoadProfile;
                return;
            }
            displayName = profile.DisplayName ?? string.Empty;
            profileStatus = profile.Status;
            profileGoal = profile.Goal;
            await LoadAvatarAsync();
        }
        catch (Exception)
        {
            _loadError = ProfileRes.FailedToLoadProfile;
        }
    }

    private async Task LoadStatsAsync()
    {
        var coursesTask = CountEnrolledCoursesAsync();
        var certificatesTask = CountCertificatesAsync();
        var assessmentsTask = CountAssessmentsAsync();
        await Task.WhenAll(coursesTask, certificatesTask, assessmentsTask);

        _enrolledCourses = coursesTask.Result;
        _certificates = certificatesTask.Result;
        _assessments = assessmentsTask.Result;
    }

    private async Task<int?> CountEnrolledCoursesAsync()
    {
        try
        {
            var courses = await CourseConsumer.GetCoursesAsync(page: 1, pageSize: CoursePageSize, lang: CurrentLang);
            return courses?.Items.Count(course => course.IsEnrolled);
        }
        catch (Exception ex)
        {
            LogStatFailed(Logger, ex, "EnrolledCourses");
            return null;
        }
    }

    private async Task<int?> CountCertificatesAsync()
    {
        try
        {
            var certificates = await CertificateConsumer.GetMyCertificatesAsync(CurrentLang);
            return certificates?.Count;
        }
        catch (Exception ex)
        {
            LogStatFailed(Logger, ex, "Certificates");
            return null;
        }
    }

    private async Task<int?> CountAssessmentsAsync()
    {
        try
        {
            var submissions = await AssessmentConsumer.GetMySubmissionsAsync(page: 1, pageSize: 1);
            return submissions?.TotalCount;
        }
        catch (Exception ex)
        {
            LogStatFailed(Logger, ex, "Assessments");
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Profile could not load the {StatName} stat tile.")]
    private static partial void LogStatFailed(ILogger logger, Exception exception, string statName);

    private async Task LoadAvatarAsync()
    {
        if (profile is null || string.IsNullOrEmpty(profile.AvatarPath))
        {
            avatarDataUrl = null;
            return;
        }

        try
        {
            var avatar = await ProfileConsumer.GetAvatarAsync();
            avatarDataUrl = avatar.HasValue
                ? $"data:{avatar.Value.ContentType};base64,{Convert.ToBase64String(avatar.Value.Data)}"
                : null;
        }
        catch
        {
            avatarDataUrl = null;
        }
    }

    private async Task HandleAvatarUpload(InputFileChangeEventArgs e)
    {
        message = string.Empty;
        var file = e.File;
        if (file == null || file.Size >= 5 * 1024 * 1024)
        {
            message = ProfileRes.FileTooLarge;
            return;
        }

        if (!AllowedAvatarTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            message = ProfileRes.InvalidImageType;
            return;
        }

        _uploadingAvatar = true;
        try
        {
            var success = await ProfileConsumer.UploadAvatarAsync(file);
            if (success)
            {
                profile = await ProfileConsumer.GetProfileAsync();
                await LoadAvatarAsync();
                AvatarNotifier.Notify();
                message = ProfileRes.AvatarUploadedSuccess;
            }
            else
            {
                message = ProfileRes.ErrorUploadingAvatar;
            }
        }
        catch (Exception ex)
        {
            message = string.Format(ProfileRes.ErrorUploadingAvatarFormat, ex.Message);
        }
        finally
        {
            _uploadingAvatar = false;
        }
    }

    private async Task UpdateProfile()
    {
        if (profile is null)
            return;

        isSaving = true;
        message = string.Empty;
        try
        {
            var updateRequest = new UpdateProfileRequest(
                profile.FirstName,
                profile.LastName,
                string.IsNullOrWhiteSpace(displayName) ? null : displayName,
                profile.DateOfBirth,
                profileStatus,
                profileGoal
            );

            var updated = await ProfileConsumer.UpdateProfileAsync(updateRequest);

            if (updated is not null)
            {
                profile = updated;
                profileStatus = updated.Status;
                profileGoal = updated.Goal;
                message = ProfileRes.ProfileUpdatedSuccess;
            }
            else
            {
                message = ProfileRes.ErrorUpdatingProfile;
            }
        }
        catch (Exception ex)
        {
            message = string.Format(ProfileRes.ErrorFormat, ex.Message);
        }
        finally
        {
            isSaving = false;
        }
    }

    private async Task ChangePassword()
    {
        if (string.IsNullOrEmpty(currentPassword) || string.IsNullOrEmpty(newPassword))
        {
            message = ProfileRes.FillAllPasswordFields;
            return;
        }

        if (newPassword != confirmPassword)
        {
            message = ProfileRes.PasswordsDoNotMatch;
            return;
        }

        isChangingPassword = true;
        message = string.Empty;
        try
        {
            var changeRequest = new ChangePasswordRequest(currentPassword, newPassword);

            var success = await ProfileConsumer.ChangePasswordAsync(changeRequest);

            if (success)
            {
                message = ProfileRes.PasswordChangedSuccess;
                currentPassword = string.Empty;
                newPassword = string.Empty;
                confirmPassword = string.Empty;
            }
            else
            {
                message = ProfileRes.ErrorChangingPassword;
            }
        }
        catch (Exception ex)
        {
            message = string.Format(ProfileRes.ErrorFormat, ex.Message);
        }
        finally
        {
            isChangingPassword = false;
        }
    }

    private async Task Logout()
    {
        var url = await AuthService.LogoutAsync();
        Navigation.NavigateTo(url, forceLoad: true);
    }

    private void RequestDeleteAccount() => _showDeleteConfirm = true;

    private void CloseDeleteDialog() => _showDeleteConfirm = false;

    private async Task DeleteAccount()
    {
        _isDeleting = true;
        _deleteError = null;
        try
        {
            var success = await ProfileConsumer.DeleteAccountAsync();
            if (success)
            {
                // The account (and its auth cookie's backing user) is gone — route through the
                // normal logout flow so the client-side auth state and cookie are cleared too.
                var url = await AuthService.LogoutAsync();
                Navigation.NavigateTo(url, forceLoad: true);
                return;
            }

            _deleteError = ProfileRes.ErrorDeletingAccount;
        }
        catch (Exception)
        {
            _deleteError = ProfileRes.ErrorDeletingAccount;
        }
        finally
        {
            _isDeleting = false;
            _showDeleteConfirm = false;
        }
    }
}

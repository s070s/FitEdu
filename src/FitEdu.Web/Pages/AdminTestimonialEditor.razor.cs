using Microsoft.AspNetCore.Components;
using FitEdu.Shared.Resources;
using FitEdu.Shared.Resources.Messages;
using Microsoft.AspNetCore.Components.Forms;
using FitEdu.Web.Consumers;
using FitEdu.Web.Extensions;
using FitEdu.Web.Shared.Components.Forms;
using FitEdu.Application.DTOs;

namespace FitEdu.Web.Pages;

public partial class AdminTestimonialEditor
{
    [Parameter] public Guid? Id { get; set; }

    [Inject] private IAdminTestimonialConsumer TestimonialConsumer { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    private bool _isEditMode => Id.HasValue;
    private bool _isBusy;
    private string? _error;
    private bool _isDirty;

    private void MarkDirty() => _isDirty = true;

    // Form fields
    private string _fullName = string.Empty;
    private string _roleOrTitle = string.Empty;
    private string _roleOrTitleEl = string.Empty;
    private string _companyOrContext = string.Empty;
    private string _quoteText = string.Empty;
    private string _quoteTextEl = string.Empty;
    private int _displayOrder = 1;
    private bool _isActive = true;

    // Avatar state
    private string? _avatarPath;
    private string? _avatarPreviewUrl;
    private FilePicker? _avatarPicker;

    /// <summary>Fallback initials shown when no avatar is set.</summary>
    private string Initials => string.IsNullOrWhiteSpace(_fullName)
        ? "?"
        : string.Concat(_fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                  .Take(2)
                                  .Select(w => char.ToUpperInvariant(w[0])));

    protected override async Task OnParametersSetAsync()
    {
        if (_isEditMode)
        {
            var item = await TestimonialConsumer.GetByIdAsync(Id!.Value);
            if (item is not null)
            {
                _fullName = item.FullName;
                _roleOrTitle = item.RoleOrTitle ?? string.Empty;
                _roleOrTitleEl = item.RoleOrTitleEl ?? string.Empty;
                _companyOrContext = item.CompanyOrContext ?? string.Empty;
                _quoteText = item.QuoteText;
                _quoteTextEl = item.QuoteTextEl ?? string.Empty;
                _displayOrder = item.DisplayOrder;
                _isActive = item.IsActive;
                _avatarPath = item.AvatarPath;
                _avatarPreviewUrl = MediaUrls.Resolve(item.AvatarPath);
            }
        }

        _isDirty = false;
    }

    private async Task OnAvatarFileSelected(InputFileChangeEventArgs e)
    {
        var file = e.File;
        if (file is null) return;

        _isBusy = true;
        _error = null;
        try
        {
            var path = await TestimonialConsumer.UploadAvatarAsync(Id!.Value, file);
            if (path is not null)
            {
                _avatarPath = path;
                _avatarPreviewUrl = MediaUrls.Resolve(path);
            }
            else
            {
                _error = AdminRes.AvatarUploadFailed;
                _avatarPicker?.ClearFileName();
            }
        }
        finally
        {
            _isBusy = false;
        }
    }

    private async Task RemoveAvatar()
    {
        _isBusy = true;
        _error = null;
        try
        {
            var success = await TestimonialConsumer.RemoveAvatarAsync(Id!.Value);
            if (success)
            {
                _avatarPath = null;
                _avatarPreviewUrl = null;
                _avatarPicker?.ClearFileName();
            }
            else
            {
                _error = AdminRes.AvatarRemoveFailed;
            }
        }
        finally
        {
            _isBusy = false;
        }
    }

    private async Task Save()
    {
        _error = null;

        if (string.IsNullOrWhiteSpace(_fullName))
        {
            _error = AdminRes.FullNameRequired;
            return;
        }
        if (string.IsNullOrWhiteSpace(_quoteText))
        {
            _error = AdminRes.QuoteRequired;
            return;
        }

        _isBusy = true;
        try
        {
            var request = new SaveTestimonialRequest(
                FullName: _fullName.Trim(),
                RoleOrTitle: TrimToNull(_roleOrTitle),
                CompanyOrContext: TrimToNull(_companyOrContext),
                QuoteText: _quoteText.Trim(),
                DisplayOrder: _displayOrder,
                IsActive: _isActive,
                RoleOrTitleEl: TrimToNull(_roleOrTitleEl),
                QuoteTextEl: TrimToNull(_quoteTextEl));

            AdminTestimonialDto? result;

            if (_isEditMode)
                result = await TestimonialConsumer.UpdateAsync(Id!.Value, request);
            else
                result = await TestimonialConsumer.CreateAsync(request);

            if (result is null)
            {
                _error = AdminRes.TestimonialSaveFailed;
                return;
            }

            _isDirty = false;
            Navigation.NavigateTo("/admin/testimonials");
        }
        catch (Exception)
        {
            _error = ErrorMessagesRes.UnexpectedErrorTryAgain;
        }
        finally
        {
            _isBusy = false;
        }
    }

    private void Cancel() => Navigation.NavigateTo("/admin/testimonials");

    /// <summary>Blank text is saved as null, so the public page falls back to the English text.</summary>
    private static string? TrimToNull(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

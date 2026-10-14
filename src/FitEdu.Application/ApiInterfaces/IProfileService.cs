using FitEdu.Application.Common;
using FitEdu.Application.DTOs;

namespace FitEdu.Application.ApiInterfaces;

/// <summary>
/// Current user's profile: get/update, avatar upload/download, and password change.
/// </summary>
public interface IProfileService
{
    Task<ProfileDto?> GetProfileAsync(string userId);
    Task<ServiceResult<ProfileDto>> UpdateProfileAsync(string userId, UpdateProfileRequest request);

    /// <summary>
    /// Stores the member's language ("en" or "el") when they switch the site language. False when the
    /// code is not supported, the user is missing or the save failed.
    /// </summary>
    Task<bool> SetPreferredLanguageAsync(string userId, string language);

    /// <summary>Deletes any existing avatar, saves the new file, and updates the user record.</summary>
    Task<ServiceResult<AvatarUploadResultDto>> UploadAvatarAsync(string userId, Stream fileStream, string fileName);
    Task<(Stream Stream, string ContentType)?> GetAvatarAsync(string userId);
    Task<ServiceResult<bool>> ChangePasswordAsync(string userId, ChangePasswordRequest request);

    /// <summary>COMP-4: aggregates the user's own personal data for a GDPR access/portability export.</summary>
    Task<MyDataExportDto?> ExportMyDataAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Completed workouts, minutes trained, the current streak and the programme to continue.</summary>
    Task<TrainingStatsDto> GetTrainingStatsAsync(string userId, CancellationToken cancellationToken = default);
}

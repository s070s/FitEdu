using Microsoft.AspNetCore.Components.Forms;
using FitEdu.Application.DTOs;

namespace FitEdu.Web.Consumers;

/// <summary>
/// Client consumer for profile-related API operations.
/// </summary>
public interface IProfileConsumer
{
    Task<ProfileDto?> GetProfileAsync();
    Task<ProfileDto?> UpdateProfileAsync(UpdateProfileRequest request);
    Task<(byte[] Data, string ContentType)?> GetAvatarAsync();
    Task<bool> UploadAvatarAsync(IBrowserFile file);
    Task<bool> ChangePasswordAsync(ChangePasswordRequest request);
    Task<bool> DeleteAccountAsync();

    /// <summary>The member's training stats for the home dashboard, or null when unavailable.</summary>
    Task<TrainingStatsDto?> GetTrainingStatsAsync();
}

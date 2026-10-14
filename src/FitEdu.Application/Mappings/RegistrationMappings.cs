using System.Globalization;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Enums;
using FitEdu.Domain.Identity;

namespace FitEdu.Application.Mappings;

/// <summary>
/// Builds the new user from a register request. Shared by the Blazor register page
/// (Infrastructure AuthService) and POST api/auth/register (AuthApiService), so both paths
/// apply the same training level, goal and language rules.
/// </summary>
public static class RegistrationMappings
{
    public static ApplicationUser ToNewUser(this RegisterRequestDto request)
    {
        // The form sends a date and time; the user entity keeps the date only.
        DateOnly? dateOfBirth = request.DateOfBirth.HasValue
            ? DateOnly.FromDateTime(request.DateOfBirth.Value.Date)
            : null;

        return new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            DateOfBirth = dateOfBirth,
            Status = TrainingLevelOrBeginner(request.Status),
            Goal = DefinedGoalOrNull(request.Goal),
            GdprConsentGiven = request.GdprConsent,
            GdprConsentDate = request.GdprConsent ? DateTime.UtcNow : null,
            // The page or API request runs in the language the member is signing up in.
            PreferredLanguage = UserLanguage.FromCulture(CultureInfo.CurrentUICulture)
        };
    }

    // Unknown is the system default, never a real choice, so it falls back to Beginner.
    private static UserStatus TrainingLevelOrBeginner(UserStatus status) =>
        status == UserStatus.Unknown ? UserStatus.Beginner : status;

    // The API takes any number for an enum, so a goal outside the list is treated as "not sure yet".
    private static FitnessGoal? DefinedGoalOrNull(FitnessGoal? goal) =>
        goal.HasValue && Enum.IsDefined(goal.Value) ? goal : null;
}

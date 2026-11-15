using Microsoft.AspNetCore.Identity;
using FitEdu.Domain.Entities;
using FitEdu.Domain.Enums;

namespace FitEdu.Domain.Identity;

/// <summary>
/// Custom user entity. Extend here instead of using raw IdentityUser.
/// </summary>
public class ApplicationUser : IdentityUser
{
    // --- Profile ---
    public required string FirstName { get; set; }
    public required string LastName { get; set; }

    public string? DisplayName { get; set; }

    public string? AvatarPath { get; set; }

    /// <summary>
    /// Stored as DATE in SQL Server. Use DateOnly for clean semantics.
    /// </summary>
    public DateOnly? DateOfBirth { get; set; }

    /// <summary>
    /// The age in whole years on <paramref name="today"/>, or null if DateOfBirth is not set. Callers
    /// pass today's date in the members' time zone (<c>SiteClock.Today</c>), not the server's UTC date,
    /// so a birthday turns at the members' midnight.
    /// </summary>
    public int? AgeOn(DateOnly today)
    {
        if (DateOfBirth is null) return null;
        var age = today.Year - DateOfBirth.Value.Year;
        if (DateOfBirth.Value > today.AddYears(-age)) age--;
        return age;
    }

    /// <summary>
    /// Self-reported training level, stored as int for forward compatibility.
    /// </summary>
    public UserStatus Status { get; set; } = UserStatus.Unknown;

    /// <summary>
    /// Optional main training goal, stored as int. Null = not chosen yet.
    /// </summary>
    public FitnessGoal? Goal { get; set; }

    /// <summary>
    /// The language the member reads FitEdu in: "en" or "el" (<see cref="UserLanguage"/>). Set at
    /// sign-up, on every language switch and on the Profile page; emails someone else triggers for
    /// the member (an admin password reset) are written in it.
    /// </summary>
    public string PreferredLanguage { get; set; } = UserLanguage.English;

    /// <summary>
    /// When false the user is locked out of the platform.
    /// Admin can toggle via /admin/users.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// UTC timestamp of the user's last presence transition (call-hub connect when going
    /// online, final disconnect when going offline). Null = never connected.
    /// </summary>
    public DateTime? LastSeenAt { get; set; }

    // --- GDPR / Compliance ---
    /// <summary>
    /// Explicit consent to data processing. Must be true for registration to complete.
    /// </summary>
    public bool GdprConsentGiven { get; set; }

    public DateTime? GdprConsentDate { get; set; }

    /// <summary>
    /// Unused: nothing reads or sets it. Sign-up refuses members under 15 (Greece's age of digital consent),
    /// so no parental-consent flow exists. The column is kept so no migration or data change is needed.
    /// </summary>
    public bool ParentalConsentGiven { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // --- Navigation Properties ---

    /// <summary>
    /// Courses the user is enrolled in.
    /// </summary>
    public ICollection<Enrollment> Enrollments { get; set; } = [];

    /// <summary>
    /// Assessment submissions by the user.
    /// </summary>
    public ICollection<AssessmentSubmission> AssessmentSubmissions { get; set; } = [];

    /// <summary>
    /// Subscription history. Only one should be active at a time.
    /// </summary>
    public ICollection<UserSubscription> UserSubscriptions { get; set; } = [];

    /// <summary>
    /// Refresh tokens for this user.
    /// </summary>
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
}

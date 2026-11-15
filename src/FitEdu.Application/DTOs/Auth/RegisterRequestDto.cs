using System.ComponentModel.DataAnnotations;
using FitEdu.Application.Common;
using FitEdu.Domain.Enums;
using FitEdu.Shared.Resources.Messages;

namespace FitEdu.Application.DTOs;

public class RegisterRequestDto
{
    /// <summary>
    /// The youngest age that may sign up: 15, Greece's age of digital consent (Law 4624/2019, article 21), so no
    /// parental consent is needed. The "You need to be at least 15" message (ErrorMessagesRes.SignUpMinimumAge)
    /// names the same number.
    /// </summary>
    public const int MinimumSignUpAge = 15;

    /// <summary>Email address (must be unique).</summary>
    /// <example>new.student@example.com</example>
    [Required(ErrorMessageResourceType = typeof(ErrorMessagesRes), ErrorMessageResourceName = nameof(ErrorMessagesRes.Auth_EmailRequired))]
    [EmailAddress(ErrorMessageResourceType = typeof(ErrorMessagesRes), ErrorMessageResourceName = nameof(ErrorMessagesRes.Auth_EmailInvalid))]
    public string Email { get; set; } = string.Empty;

    /// <summary>Password: min 8 chars, at least one uppercase letter, one lowercase letter and one digit — must
    /// match the Identity policy configured in AuthenticationSetupExtensions (RequiredLength/RequireDigit/RequireUppercase; RequireLowercase is Identity's default).</summary>
    /// <example>P@ssw0rd123</example>
    [Required(ErrorMessageResourceType = typeof(ErrorMessagesRes), ErrorMessageResourceName = nameof(ErrorMessagesRes.Auth_PasswordRequired))]
    [RegularExpression(PasswordRules.ComplexityPattern, ErrorMessageResourceType = typeof(ErrorMessagesRes), ErrorMessageResourceName = nameof(ErrorMessagesRes.Auth_PasswordComplexity))]
    public string Password { get; set; } = string.Empty;

    /// <summary>Must match <see cref="Password"/>.</summary>
    /// <example>P@ssw0rd123</example>
    [Required(ErrorMessageResourceType = typeof(ErrorMessagesRes), ErrorMessageResourceName = nameof(ErrorMessagesRes.Auth_ConfirmPasswordRequired))]
    [Compare(nameof(Password), ErrorMessageResourceType = typeof(ErrorMessagesRes), ErrorMessageResourceName = nameof(ErrorMessagesRes.Auth_PasswordMismatch))]
    public string ConfirmPassword { get; set; } = string.Empty;

    /// <summary>Given name.</summary>
    /// <example>Maria</example>
    [Required(ErrorMessageResourceType = typeof(ErrorMessagesRes), ErrorMessageResourceName = nameof(ErrorMessagesRes.Auth_FirstNameRequired))]
    [MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    /// <summary>Family name.</summary>
    /// <example>Papadopoulou</example>
    [Required(ErrorMessageResourceType = typeof(ErrorMessagesRes), ErrorMessageResourceName = nameof(ErrorMessagesRes.Auth_LastNameRequired))]
    [MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// Required: a sign-up needs an age of at least <see cref="MinimumSignUpAge"/> on the members' date, and a
    /// date in the future is refused. Use DateTime (client &amp; API). Date-only semantics are stored as DateOnly
    /// on the user entity.
    /// </summary>
    /// <example>1998-04-12T00:00:00Z</example>
    [Required(ErrorMessageResourceType = typeof(ErrorMessagesRes), ErrorMessageResourceName = nameof(ErrorMessagesRes.Auth_DateOfBirthRequired))]
    public DateTime? DateOfBirth { get; set; }

    /// <summary>
    /// Must be true to register. GDPR requirement.
    /// </summary>
    /// <example>true</example>
    [Range(typeof(bool), "true", "true", ErrorMessageResourceType = typeof(ErrorMessagesRes), ErrorMessageResourceName = nameof(ErrorMessagesRes.Auth_GdprConsentRequired))]
    public bool GdprConsent { get; set; }

    /// <summary>
    /// Optional training level (Beginner/Intermediate/Advanced/Other). Defaults to Beginner;
    /// <see cref="UserStatus.Unknown"/> is coerced back to Beginner server-side since it is
    /// not a real user-facing option.
    /// </summary>
    /// <example>Beginner</example>
    public UserStatus Status { get; set; } = UserStatus.Beginner;

    /// <summary>
    /// Optional main training goal. Null means "not sure yet"; a value outside
    /// <see cref="FitnessGoal"/> is dropped server-side.
    /// </summary>
    /// <example>Strength</example>
    public FitnessGoal? Goal { get; set; }
}

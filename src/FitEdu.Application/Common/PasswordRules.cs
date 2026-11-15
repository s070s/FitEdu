namespace FitEdu.Application.Common;

/// <summary>
/// The one source of the password rules. The Identity options (AuthenticationSetupExtensions), the
/// request DTOs and the common-password check all read the minimum length from here. The texts in the
/// resource files that name the number ("at least 8 characters") are written by hand: change them
/// together with <see cref="MinimumLength"/>.
/// </summary>
public static class PasswordRules
{
    public const int MinimumLength = 8;

    /// <summary>The longest password the profile form accepts.</summary>
    public const int MaximumLength = 128;

    // A constant string cannot be built from the numbers above (attribute arguments must be constants),
    // so the two patterns repeat them. CommonPasswordTests fails when a pattern drifts from the numbers.

    /// <summary>The form and API rule: the minimum length, a capital letter, a lowercase letter and a digit.</summary>
    public const string ComplexityPattern = @"^(?=.*[A-Z])(?=.*[a-z])(?=.*[0-9]).{8,}$";

    /// <summary>The same rule with the maximum length, for the profile's change-password form.</summary>
    public const string ComplexityPatternWithMaximum = @"^(?=.*[A-Z])(?=.*[a-z])(?=.*[0-9]).{8,128}$";
}

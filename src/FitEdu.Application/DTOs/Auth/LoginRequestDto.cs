using System.ComponentModel.DataAnnotations;
using FitEdu.Shared.Resources.Messages;

namespace FitEdu.Application.DTOs;

public class LoginRequestDto
{
    /// <summary>Registered email address.</summary>
    /// <example>student@example.com</example>
    [Required(ErrorMessageResourceType = typeof(ErrorMessagesRes), ErrorMessageResourceName = nameof(ErrorMessagesRes.Auth_EmailRequired))]
    [EmailAddress(ErrorMessageResourceType = typeof(ErrorMessagesRes), ErrorMessageResourceName = nameof(ErrorMessagesRes.Auth_EmailInvalid))]
    public string Email { get; set; } = string.Empty;

    /// <summary>Account password.</summary>
    /// <example>P@ssw0rd123</example>
    [Required(ErrorMessageResourceType = typeof(ErrorMessagesRes), ErrorMessageResourceName = nameof(ErrorMessagesRes.Auth_PasswordRequired))]
    public string Password { get; set; } = string.Empty;

    /// <summary>When true, issues a 30-day refresh token instead of 7 days.</summary>
    /// <example>true</example>
    public bool RememberMe { get; set; }
}

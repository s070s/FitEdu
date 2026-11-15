using System.ComponentModel.DataAnnotations;
using FitEdu.Shared.Resources.Messages;

namespace FitEdu.Application.DTOs;

public class ResendConfirmationRequestDto
{
    /// <summary>The email address the confirmation link was sent to.</summary>
    /// <example>student@example.com</example>
    [Required(ErrorMessageResourceType = typeof(ErrorMessagesRes), ErrorMessageResourceName = nameof(ErrorMessagesRes.Auth_EmailRequired))]
    [EmailAddress(ErrorMessageResourceType = typeof(ErrorMessagesRes), ErrorMessageResourceName = nameof(ErrorMessagesRes.Auth_EmailInvalid))]
    public string Email { get; set; } = string.Empty;
}

using System.ComponentModel.DataAnnotations;
using FitEdu.Application.Common;

namespace FitEdu.Application.DTOs;

public class DevResetPasswordRequestDto
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(PasswordRules.MinimumLength)]
    public string NewPassword { get; set; } = string.Empty;
}

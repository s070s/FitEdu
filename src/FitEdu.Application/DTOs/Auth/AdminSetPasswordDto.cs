using System.ComponentModel.DataAnnotations;
using FitEdu.Application.Common;

namespace FitEdu.Application.DTOs;

public class AdminSetPasswordDto
{
    [Required, MinLength(PasswordRules.MinimumLength)]
    public string NewPassword { get; set; } = string.Empty;
}

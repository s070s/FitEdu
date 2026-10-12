using System.ComponentModel.DataAnnotations;

namespace FitEdu.Application.DTOs;

public class ConfirmEmailRequestDto
{
    [Required]
    public string UserId { get; set; } = string.Empty;

    [Required]
    public string Token { get; set; } = string.Empty;
}

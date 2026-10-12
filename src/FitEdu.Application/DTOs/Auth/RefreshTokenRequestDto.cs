using System.ComponentModel.DataAnnotations;

namespace FitEdu.Application.DTOs;

public class RefreshTokenRequestDto
{
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}

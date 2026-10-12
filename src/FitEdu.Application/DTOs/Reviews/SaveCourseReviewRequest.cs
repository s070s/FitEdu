using System.ComponentModel.DataAnnotations;

namespace FitEdu.Application.DTOs;

public record SaveCourseReviewRequest(
    [Range(1, 5)] int Rating,
    [Required, MaxLength(2000)] string Body
);

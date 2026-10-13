using FitEdu.Application.DTOs;

namespace FitEdu.Web.Consumers;

/// <summary>
/// Client consumer for the public testimonials API.
/// Returns TestimonialDto where AvatarUrl is already a fully-qualified URL.
/// </summary>
public interface ITestimonialConsumer
{
    /// <summary>Active testimonials with role and quote in <paramref name="lang"/> ("en" or "el").</summary>
    Task<IReadOnlyList<TestimonialDto>?> GetActiveAsync(string lang = "en", CancellationToken ct = default);
}

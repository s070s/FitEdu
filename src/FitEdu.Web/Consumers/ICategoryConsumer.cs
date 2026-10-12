using FitEdu.Application.DTOs;

namespace FitEdu.Web.Consumers;

/// <summary>
/// Client consumer for public category discovery (browse/filter chips).
/// </summary>
public interface ICategoryConsumer
{
    Task<List<CategoryDto>> GetCategoriesAsync(string scope, string lang = "en");
}

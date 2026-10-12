using FitEdu.Application.DTOs;

namespace FitEdu.Web.Consumers;

/// <summary>
/// Client consumer for the site search API.
/// </summary>
public interface ISearchConsumer
{
    Task<SiteSearchResultDto?> SearchAsync(string query, int limit, string lang);
}

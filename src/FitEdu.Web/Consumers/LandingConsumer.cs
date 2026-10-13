using FitEdu.Application.DTOs;

namespace FitEdu.Web.Consumers;

/// <summary>HTTP consumer for the public home page API.</summary>
public class LandingConsumer(HttpClient http, FitEdu.Web.Services.ApiTokenProvider tokenProvider) : ApiClientBase(http, tokenProvider), ILandingConsumer
{
    public Task<LandingOverviewDto?> GetOverviewAsync(string lang = "en")
        => GetAsync<LandingOverviewDto>($"api/landing?lang={lang}");
}

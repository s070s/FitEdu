using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using FitEdu.Web.Startup;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

public class ServiceRegistrationExtensionsTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public ServiceRegistrationExtensionsTests(CustomWebAppFactory factory) => _factory = factory;

    [Fact]
    public void GreekCulture_UsesA24HourClock()
    {
        var greek = ServiceRegistrationExtensions.CreateGreekCulture();
        var ninePm = new DateTime(2026, 10, 20, 21, 0, 0);

        greek.Name.ShouldBe("el-GR");
        ninePm.ToString("t", greek).ShouldBe("21:00");
        ninePm.ToString("T", greek).ShouldBe("21:00:00");
    }

    [Fact]
    public void RequestLocalization_OffersTheGreekCultureWithTheTwentyFourHourClock()
    {
        var options = _factory.Services.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value;

        var greek = options.SupportedCultures.ShouldNotBeNull().Single(culture => culture.Name == "el-GR");
        greek.DateTimeFormat.ShortTimePattern.ShouldBe("HH:mm");
        options.SupportedUICultures.ShouldNotBeNull().ShouldContain(greek);
        options.DefaultRequestCulture.Culture.Name.ShouldBe("en-GB");
    }

    [Fact]
    public void ResolveSelfBaseUrl_Development_DefaultsToLocalhost_WhenUnset()
    {
        var result = ServiceRegistrationExtensions.ResolveSelfBaseUrl(null, isDevelopment: true);

        result.ShouldBe("https://localhost:7090");
    }

    [Fact]
    public void ResolveSelfBaseUrl_Development_UsesConfiguredValue_WhenSet()
    {
        var result = ServiceRegistrationExtensions.ResolveSelfBaseUrl("https://dev.example", isDevelopment: true);

        result.ShouldBe("https://dev.example");
    }

    [Fact]
    public void ResolveSelfBaseUrl_NonDevelopment_ReturnsConfiguredValue()
    {
        var result = ServiceRegistrationExtensions.ResolveSelfBaseUrl("https://fitedu.com", isDevelopment: false);

        result.ShouldBe("https://fitedu.com");
    }

    [Fact]
    public void ResolveSelfBaseUrl_NonDevelopment_ThrowsWhenUnset()
    {
        Should.Throw<InvalidOperationException>(() => ServiceRegistrationExtensions.ResolveSelfBaseUrl(null, isDevelopment: false));
    }

    [Fact]
    public void ResolveSelfBaseUrl_NonDevelopment_ThrowsWhenBlank()
    {
        Should.Throw<InvalidOperationException>(() => ServiceRegistrationExtensions.ResolveSelfBaseUrl("   ", isDevelopment: false));
    }

    [Fact]
    public void ResolveSelfBaseUrl_NonDevelopment_ThrowsWhenStillPointsAtLocalhost()
    {
        Should.Throw<InvalidOperationException>(() => ServiceRegistrationExtensions.ResolveSelfBaseUrl("https://localhost:7090", isDevelopment: false));
    }
}

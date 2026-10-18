using Microsoft.Extensions.Diagnostics.HealthChecks;
using FitEdu.Application.Common;
using FitEdu.Web.Services;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>The "assistant" readiness check reports Degraded until the coach can answer.</summary>
public class AssistantHealthCheckTests
{
    private static async Task<HealthStatus> StatusFor(AssistantAvailability availability)
    {
        var state = new AssistantRuntimeState();
        state.Set(availability);
        var result = await new AssistantHealthCheck(state).CheckHealthAsync(new HealthCheckContext());
        return result.Status;
    }

    [Theory]
    [InlineData(AssistantAvailability.Starting)] // live check 2026-10-18: reported Healthy while the model loaded
    [InlineData(AssistantAvailability.OllamaUnreachable)]
    [InlineData(AssistantAvailability.DownloadingModels)]
    public async Task NotReadyYet_IsDegraded(AssistantAvailability availability) =>
        (await StatusFor(availability)).ShouldBe(HealthStatus.Degraded);

    [Theory]
    [InlineData(AssistantAvailability.Ready)]
    [InlineData(AssistantAvailability.Disabled)]
    public async Task ReadyOrSwitchedOff_IsHealthy(AssistantAvailability availability) =>
        (await StatusFor(availability)).ShouldBe(HealthStatus.Healthy);
}

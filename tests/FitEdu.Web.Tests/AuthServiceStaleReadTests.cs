using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Identity;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// A Blazor circuit keeps one DbContext for its whole life (P02-13). A member read earlier in the circuit
/// stays in its change tracker, so after the email is confirmed elsewhere (a second tab, the email link) the
/// circuit would still see it unconfirmed. Each page-service call must therefore read fresh data. The tests
/// use one DI scope as the "circuit" and a second scope as "elsewhere", with the real UserManager.
/// </summary>
public class AuthServiceStaleReadTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public AuthServiceStaleReadTests(CustomWebAppFactory factory) => _factory = factory;

    private (WebApplicationFactory<Program> Host, IEmailService Email) HostWithRecordedEmails()
    {
        var email = Substitute.For<IEmailService>();
        var host = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton(email)));
        return (host, email);
    }

    // The circuit reads the member (unconfirmed, so tracked as such), then the confirmation happens in another scope.
    private static async Task<string> ReadUnconfirmedInTheCircuitThenConfirmElsewhere(IServiceScope circuit, IServiceProvider elsewhereServices)
    {
        var address = $"stale-{Guid.NewGuid():N}@test.com";
        var circuitUsers = circuit.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var member = new ApplicationUser { UserName = address, Email = address, FirstName = "Stale", LastName = "Read", IsEnabled = true };
        (await circuitUsers.CreateAsync(member, CustomWebAppFactory.TestPassword)).Succeeded.ShouldBeTrue();
        (await circuitUsers.IsEmailConfirmedAsync(member)).ShouldBeFalse();

        using var elsewhere = elsewhereServices.CreateScope();
        var elsewhereUsers = elsewhere.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var sameMember = (await elsewhereUsers.FindByEmailAsync(address))!;
        sameMember.EmailConfirmed = true;
        (await elsewhereUsers.UpdateAsync(sameMember)).Succeeded.ShouldBeTrue();
        return address;
    }

    [Fact]
    public async Task ForgotPassword_AfterStaleRead_SendsTheReset()
    {
        var (host, email) = HostWithRecordedEmails();
        using var _ = host;
        using var circuit = host.Services.CreateScope();
        var address = await ReadUnconfirmedInTheCircuitThenConfirmElsewhere(circuit, host.Services);

        await circuit.ServiceProvider.GetRequiredService<IAuthService>()
            .ForgotPasswordAsync(new ForgotPasswordRequestDto { Email = address });

        await email.Received(1).SendPasswordResetAsync(address, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}

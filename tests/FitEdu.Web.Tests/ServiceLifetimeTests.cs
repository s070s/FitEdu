using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Web.Interfaces;
using FitEdu.Web.Services;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// Pins the lifetimes the realtime services depend on (MAINT-10, ARCH-8): the per-circuit call state must stay
/// shared by the components of one circuit, and the process-wide trackers must outlive every circuit.
/// </summary>
public class ServiceLifetimeTests : IClassFixture<ServiceLifetimeTests.RegistrationCapturingFactory>
{
    private readonly RegistrationCapturingFactory _factory;

    public ServiceLifetimeTests(RegistrationCapturingFactory factory) => _factory = factory;

    [Theory]
    [InlineData(typeof(ICallService), ServiceLifetime.Scoped)]
    [InlineData(typeof(PresenceService), ServiceLifetime.Scoped)]
    [InlineData(typeof(CallRegistry), ServiceLifetime.Singleton)]
    [InlineData(typeof(NotificationConnectionTracker), ServiceLifetime.Singleton)]
    [InlineData(typeof(LiveConnectionRegistry), ServiceLifetime.Singleton)]
    [InlineData(typeof(ILiveConnectionCloser), ServiceLifetime.Singleton)]
    [InlineData(typeof(IAuthAttemptLimiter), ServiceLifetime.Singleton)]
    // ChatService is a typed HttpClient (transient), unlike the scoped CallService: each component that injects it gets
    // its own instance and hub connection. It works today; a change to either lifetime has to be a deliberate one.
    [InlineData(typeof(IChatService), ServiceLifetime.Transient)]
    public void RealtimeServices_KeepTheirLifetime(Type serviceType, ServiceLifetime expected)
    {
        _factory.Registrations.Single(descriptor => descriptor.ServiceType == serviceType).Lifetime.ShouldBe(expected);
    }

    [Fact]
    public void FileStorage_IsOneInstanceForTheWholeApp()
    {
        // It holds only singleton services and creates the uploads folder in its constructor (P05-18).
        _factory.Registrations.Single(descriptor => descriptor.ServiceType == typeof(IFileStorage))
            .Lifetime.ShouldBe(ServiceLifetime.Singleton);
    }

    [Fact]
    public void LiveConnectionCloser_IsTheSameInstanceTheHubsRegisterOn()
    {
        _factory.Services.GetRequiredService<ILiveConnectionCloser>()
            .ShouldBeSameAs(_factory.Services.GetRequiredService<LiveConnectionRegistry>());
    }

    [Fact]
    public async Task CallService_InOneScope_IsOneInstance_InAnotherScopeAnotherOne()
    {
        // CallService is async-disposable only, so the scopes must be disposed asynchronously.
        await using var circuit = _factory.Services.CreateAsyncScope();
        await using var otherCircuit = _factory.Services.CreateAsyncScope();

        var first = circuit.ServiceProvider.GetRequiredService<ICallService>();
        var second = circuit.ServiceProvider.GetRequiredService<ICallService>();
        var inOtherCircuit = otherCircuit.ServiceProvider.GetRequiredService<ICallService>();

        second.ShouldBeSameAs(first);
        inOtherCircuit.ShouldNotBeSameAs(first);
    }

    /// <summary>A test host that keeps the final service collection, so the registered lifetimes can be read.</summary>
    public sealed class RegistrationCapturingFactory : CustomWebAppFactory
    {
        private IServiceCollection? _registrations;

        public IServiceCollection Registrations
        {
            get
            {
                // The collection is only handed over while the host builds.
                _ = Services;
                return _registrations!;
            }
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services => _registrations = services);
        }
    }
}

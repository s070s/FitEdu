using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace FitEdu.Web.Tests;

/// <summary>
/// Like <see cref="CustomWebAppFactory"/>, but the pages' API consumers reach this same in-memory
/// test server instead of the real loopback address (nothing listens there in tests), so a
/// prerendered page shows the data the test seeded.
/// </summary>
public class SelfCallingWebAppFactory : CustomWebAppFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
            services.ConfigureAll<HttpClientFactoryOptions>(options =>
                options.HttpMessageHandlerBuilderActions.Add(handlerBuilder =>
                {
                    var testServer = (TestServer)handlerBuilder.Services.GetRequiredService<IServer>();
                    handlerBuilder.PrimaryHandler = testServer.CreateHandler();
                })));
    }
}

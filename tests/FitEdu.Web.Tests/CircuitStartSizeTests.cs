using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using FitEdu.Application.DTOs;
using FitEdu.TestSupport;
using FitEdu.Web.Startup;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace FitEdu.Web.Tests;

/// <summary>
/// The message that starts a Blazor circuit carries the prerendered root component descriptors and
/// the page's persisted state, and Greek text grows about six-fold in that state. These tests render
/// the heaviest public pages in Greek with the seeded content and check that the message fits the
/// circuit hub's limit: a larger message closes the connection and the page never turns interactive.
/// </summary>
public partial class CircuitStartSizeTests : IClassFixture<CircuitStartSizeTests.LoopbackWebAppFactory>
{
    private const string GreekCulture = "el-GR";

    // The base URI, the page URI and the message framing, on top of the state and the descriptors.
    private const int MessageOverheadBytes = 1024;

    // An empty persisted state is a few hundred characters; seeded blog posts and stories add thousands.
    private const int MinimumSeededStateLength = 2_000;

    private readonly LoopbackWebAppFactory _factory;
    private readonly ITestOutputHelper _output;

    public CircuitStartSizeTests(LoopbackWebAppFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    public static TheoryData<string> HeaviestGreekPages() => new("/", "/blog", "/blog/" + LongestGreekArticleSlug());

    [Theory]
    [MemberData(nameof(HeaviestGreekPages))]
    public async Task GreekPage_CircuitStartMessageFitsTheCircuitHubLimit(string path)
    {
        var html = await GetGreekPageAsync(path);

        var persistedState = PersistedStateRegex().Match(html).Groups["state"].Value;
        var descriptorsLength = ServerComponentMarkerRegex().Matches(html).Sum(marker => marker.Length);
        var estimatedMessageBytes = persistedState.Length + descriptorsLength + MessageOverheadBytes;
        _output.WriteLine($"{path}: state {persistedState.Length} + descriptors {descriptorsLength} characters");

        persistedState.Length.ShouldBeGreaterThan(MinimumSeededStateLength, "the page should persist the seeded content");
        estimatedMessageBytes.ShouldBeLessThan(ServiceRegistrationExtensions.CircuitMessageLimitBytes,
            $"{path} starts its circuit with about {estimatedMessageBytes} bytes");
    }

    [Fact]
    public async Task GreekHome_PrerendersTheSeededBlogPostsAndStories()
    {
        var html = await GetGreekPageAsync("/");

        // Proves the loopback API calls reach the seeded data, so the size test measures real content.
        html.ShouldContain("blog-card");
        html.ShouldContain("testimonials-grid");
        // The guest landing API (anonymous) reaches the seeded catalog too: numbers and featured courses.
        html.ShouldContain("stats-strip");
        html.ShouldContain("section-featured");
    }

    [Fact]
    public void OnlyTheCircuitHubAcceptsTheLargerMessages()
    {
        // ComponentHub is internal, so its options type is built from the Type.
        var componentHubType = typeof(CircuitOptions).Assembly
            .GetType("Microsoft.AspNetCore.Components.Server.ComponentHub", throwOnError: true)!;
        var componentHubOptionsType = typeof(IOptions<>).MakeGenericType(typeof(HubOptions<>).MakeGenericType(componentHubType));
        var componentHubOptions = (HubOptions)componentHubOptionsType.GetProperty(nameof(IOptions<HubOptions>.Value))!
            .GetValue(_factory.Services.GetRequiredService(componentHubOptionsType))!;

        // The chat, call and notification hubs set no options of their own, so the global ones apply.
        var globalHubOptions = _factory.Services.GetRequiredService<IOptions<HubOptions>>().Value;

        componentHubOptions.MaximumReceiveMessageSize.ShouldBe(ServiceRegistrationExtensions.CircuitMessageLimitBytes);
        globalHubOptions.MaximumReceiveMessageSize.ShouldBe(ServiceRegistrationExtensions.AppHubMessageLimitBytes);
    }

    private async Task<string> GetGreekPageAsync(string path)
    {
        var cookieValue = CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(GreekCulture));
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", $"{CookieRequestCultureProvider.DefaultCookieName}={Uri.EscapeDataString(cookieValue)}");

        using var client = _factory.CreateClient();
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private static string LongestGreekArticleSlug() =>
        BlogSeedData.SeedArticles.MaxBy(article => article.ContentEl?.Length ?? 0)!.Slug;

    // The persisted state Blazor writes for the circuit, as one base64 comment.
    [GeneratedRegex(@"<!--Blazor-Server-Component-State:(?<state>.*?)-->", RegexOptions.Singleline)]
    private static partial Regex PersistedStateRegex();

    // A server root component's start marker, which carries its encrypted descriptor.
    [GeneratedRegex(@"<!--Blazor:\{[^>]*?""descriptor"":[^>]*?\}-->")]
    private static partial Regex ServerComponentMarkerRegex();

    /// <summary>
    /// The real app with the seeded blog posts and member stories. Its pages call the API through
    /// loopback HttpClients; those calls are sent into this test server instead of the network.
    /// </summary>
    public sealed class LoopbackWebAppFactory : CustomWebAppFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            // The Development seed paths are relative to the working folder, which is the test bin folder.
            // The member stories seed with the live classes, which name courses, whose lessons name exercises.
            var seedFolders = new Dictionary<string, string?>
            {
                ["SeedData:JsonPaths:Exercises"] = RepoPaths.ExerciseSeedFolder,
                ["SeedData:JsonPaths:Courses"] = RepoPaths.CourseSeedFolder,
                ["SeedData:JsonPaths:Showcase"] = Path.Combine(RepoPaths.SeedJsonRoot, "Showcase"),
            };
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(seedFolders));

            builder.ConfigureTestServices(services => services.ConfigureHttpClientDefaults(client =>
                client.ConfigurePrimaryHttpMessageHandler(() => Server.CreateHandler())));
        }
    }
}

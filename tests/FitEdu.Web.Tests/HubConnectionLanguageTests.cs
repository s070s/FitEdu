using System.Globalization;
using System.Net.WebSockets;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Client;
using FitEdu.Application.ApiServices;
using FitEdu.Application.Common;
using FitEdu.Shared.Resources.Messages;
using FitEdu.TestSupport;
using FitEdu.Web.Services;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// Chat and call errors are pushed over SignalR, not the loopback API, so the hub connection must carry
/// the member's UI language itself. These tests connect to the real hubs the way ChatService and
/// CallService do and check that the error text comes back in the page language.
/// </summary>
public class HubConnectionLanguageTests : IClassFixture<CustomWebAppFactory>
{
    private static readonly TimeSpan ErrorTimeout = TimeSpan.FromSeconds(10);

    private readonly CustomWebAppFactory _factory;

    public HubConnectionLanguageTests(CustomWebAppFactory factory) => _factory = factory;

    [Fact]
    public void SendCurrentUiLanguage_SetsTheAcceptLanguageHeader()
    {
        using var cultureScope = new CultureScope("el-GR");
        var options = new HttpConnectionOptions();

        HubConnectionLanguage.SendCurrentUiLanguage(options);

        options.Headers["Accept-Language"].ShouldBe("el-GR");
    }

    [Theory]
    [InlineData("el-GR")]
    [InlineData("en-GB")]
    public async Task CallHub_ErrorFollowsTheMemberLanguage(string cultureName)
    {
        using var cultureScope = new CultureScope(cultureName);
        var expectedError = ErrorMessagesRes.ResourceManager.GetString(nameof(ErrorMessagesRes.Call_NotInCall), CultureInfo.CurrentUICulture);
        await using var connection = await ConnectAsync("hubs/call");
        var callError = WaitForMessage(connection, "CallError");

        // Relaying an offer for a call the member is not in is rejected with Call_NotInCall.
        await connection.InvokeAsync("SendOffer", Guid.NewGuid(), "unknown-connection", new { });

        (await callError.WaitAsync(ErrorTimeout)).ShouldBe(expectedError);
    }

    [Theory]
    [InlineData("el-GR")]
    [InlineData("en-GB")]
    public async Task ChatHub_ErrorFollowsTheMemberLanguage(string cultureName)
    {
        using var cultureScope = new CultureScope(cultureName);
        var expectedError = ResourceFormat.Fill(
            ErrorMessagesRes.ResourceManager.GetString(nameof(ErrorMessagesRes.Chat_MessageTooLongFormat), CultureInfo.CurrentUICulture)!,
            ChatCommandService.MaxMessageLength);
        await using var connection = await ConnectAsync("hubs/chat");
        var chatError = WaitForMessage(connection, "ChatError");

        // The length check runs before the conversation lookup, so any conversation id gets the error.
        var tooLongMessage = new string('a', ChatCommandService.MaxMessageLength + 1);
        await connection.InvokeAsync("SendMessage", Guid.NewGuid(), tooLongMessage);

        (await chatError.WaitAsync(ErrorTimeout)).ShouldBe(expectedError);
    }

    private static Task<string> WaitForMessage(HubConnection connection, string methodName)
    {
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<string>(methodName, message => received.TrySetResult(message));
        return received.Task;
    }

    /// <summary>Connects a member to the hub over the test server's WebSocket, configured like the app's hub services.</summary>
    private async Task<HubConnection> ConnectAsync(string hubPath)
    {
        var memberClient = await _factory.CreateAuthenticatedClientAsync("Student");
        var accessToken = memberClient.DefaultRequestHeaders.Authorization!.Parameter!;
        var hubUrl = new Uri(_factory.Server.BaseAddress, hubPath);

        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                HubConnectionLanguage.SendCurrentUiLanguage(options);
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
                options.WebSocketFactory = (context, cancellationToken) =>
                    ConnectWebSocketAsync(context, accessToken, cancellationToken);
            })
            .Build();

        await connection.StartAsync();
        return connection;
    }

    private async ValueTask<WebSocket> ConnectWebSocketAsync(
        WebSocketConnectionContext context, string accessToken, CancellationToken cancellationToken)
    {
        var webSocketClient = _factory.Server.CreateWebSocketClient();
        webSocketClient.ConfigureRequest = request =>
        {
            request.Headers.Authorization = $"Bearer {accessToken}";
            // The real WebSockets transport sends the connection's headers on the upgrade request.
            foreach (var header in context.Options.Headers)
                request.Headers[header.Key] = header.Value;
        };

        return await webSocketClient.ConnectAsync(context.Uri, cancellationToken);
    }
}

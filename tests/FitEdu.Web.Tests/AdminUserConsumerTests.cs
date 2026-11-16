using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Components.Authorization;
using NSubstitute;
using FitEdu.Application.ApiInterfaces;
using FitEdu.TestSupport;
using FitEdu.Web.Consumers;
using FitEdu.Web.Services;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// The admin user consumer's write calls tell the page why the API refused: the status code and the
/// problem-details text the API sends (see ServiceResultExtensions).
/// </summary>
public class AdminUserConsumerTests
{
    private static ApiTokenProvider TokenProvider()
    {
        var authState = Substitute.For<AuthenticationStateProvider>();
        authState.GetAuthenticationStateAsync()
            .Returns(Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()))));
        return new ApiTokenProvider(authState, Substitute.For<IAuthService>());
    }

    private static (AdminUserConsumer Consumer, StubHttpMessageHandler Handler) ConsumerAnswering(
        HttpStatusCode status, string? body = null, string mediaType = "application/problem+json")
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(status)
        {
            Content = body is null ? new StringContent(string.Empty) : new StringContent(body, Encoding.UTF8, mediaType)
        });
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        return (new AdminUserConsumer(client, TokenProvider()), handler);
    }

    [Fact]
    public async Task SetPassword_Accepted_Succeeds()
    {
        var (consumer, handler) = ConsumerAnswering(HttpStatusCode.OK);

        var result = await consumer.SetPasswordAsync("member-1", "Passw0rd");

        result.ShouldBe(ApiActionResult.Success);
        handler.LastRequest!.Method.ShouldBe(HttpMethod.Post);
        handler.LastRequest.RequestUri!.PathAndQuery.ShouldBe("/api/admin/users/member-1/set-password");
    }

    [Fact]
    public async Task SetPassword_RefusedWithAProblemBody_CarriesTheStatusAndTheServersDetail()
    {
        const string body = """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"Bad Request","status":400,"detail":"Passwords must have at least one digit ('0'-'9').","traceId":"00-abc"}""";
        var (consumer, _) = ConsumerAnswering(HttpStatusCode.BadRequest, body);

        var result = await consumer.SetPasswordAsync("member-1", "Password");

        result.Succeeded.ShouldBeFalse();
        result.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        result.Detail.ShouldBe("Passwords must have at least one digit ('0'-'9').");
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>Bad gateway</html>")]
    [InlineData("""{"title":"One or more validation errors occurred.","status":400}""")]
    public async Task SetPassword_RefusedWithoutADetail_HasTheStatusAndNoDetail(string body)
    {
        var (consumer, _) = ConsumerAnswering(HttpStatusCode.BadRequest, body, mediaType: "text/html");

        var result = await consumer.SetPasswordAsync("member-1", "Password");

        result.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        result.Detail.ShouldBeNull();
    }

    [Fact]
    public async Task Delete_Conflict_CarriesTheConflictStatus()
    {
        var (consumer, handler) = ConsumerAnswering(HttpStatusCode.Conflict,
            """{"title":"Conflict","status":409,"detail":"User could not be deleted because other data still references the account."}""");

        var result = await consumer.DeleteUserAsync("member-1");

        result.Succeeded.ShouldBeFalse();
        result.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        handler.LastRequest!.Method.ShouldBe(HttpMethod.Delete);
        handler.LastRequest.RequestUri!.PathAndQuery.ShouldBe("/api/admin/users/member-1");
    }

    [Fact]
    public async Task Delete_NoContent_Succeeds()
    {
        var (consumer, _) = ConsumerAnswering(HttpStatusCode.NoContent);

        (await consumer.DeleteUserAsync("member-1")).ShouldBe(ApiActionResult.Success);
    }

    [Fact]
    public async Task SetPassword_ConnectionRefused_IsUnreachable()
    {
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("Connection refused"));
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var consumer = new AdminUserConsumer(client, TokenProvider());

        var result = await consumer.SetPasswordAsync("member-1", "Passw0rd");

        result.ShouldBe(ApiActionResult.Unreachable);
    }
}

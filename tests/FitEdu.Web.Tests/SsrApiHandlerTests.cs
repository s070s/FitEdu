using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using FitEdu.Infrastructure.ApiServices;
using FitEdu.Web.Services;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// The handler signs a short-lived bearer token from the signed-in user's claims, so the app's own
/// API calls from a Blazor page reach the controllers as that user (and anonymous visitors send none).
/// </summary>
public class SsrApiHandlerTests
{
    private const string SigningKey = "ssr-handler-test-signing-key-at-least-32-bytes-long";

    private static readonly JwtOptions Jwt = new()
    {
        Key = SigningKey,
        Issuer = "ssr-issuer",
        Audience = "ssr-audience",
        AccessTokenExpirationMinutes = 15
    };

    /// <summary>Answers every request with 200 and keeps the request, so the test can read the headers it carried.</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private static async Task<HttpRequestMessage> SendAsHandlerAsync(ClaimsPrincipal? user)
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(user is null ? null : new DefaultHttpContext { User = user });
        var capture = new CapturingHandler();
        var handler = new SsrApiHandler(accessor, new JwtTokenSigner(Options.Create(Jwt))) { InnerHandler = capture };

        using var client = new HttpClient(handler);
        using var response = await client.GetAsync("http://localhost/api/profile");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return capture.Request!;
    }

    private static ClaimsPrincipal SignedInMember() =>
        new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "user-1"),
                new Claim(ClaimTypes.Role, "Student"),
                new Claim("securityStamp", "stamp-1"),
                new Claim("impersonatedBy", "admin-9")
            ],
            "cookie"));

    [Fact]
    public async Task Send_SignedInUser_AttachesABearerTokenSignedWithTheConfiguredKey()
    {
        var request = await SendAsHandlerAsync(SignedInMember());

        request.Headers.Authorization!.Scheme.ShouldBe("Bearer");
        var parameters = new TokenValidationParameters
        {
            ValidIssuer = Jwt.Issuer,
            ValidAudience = Jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey))
        };
        new JwtSecurityTokenHandler().ValidateToken(request.Headers.Authorization.Parameter, parameters, out _);
    }

    [Fact]
    public async Task Send_SignedInUser_TheTokenCarriesTheUsersClaimsAndTheConfiguredLifetime()
    {
        var request = await SendAsHandlerAsync(SignedInMember());

        var token = new JwtSecurityTokenHandler().ReadJwtToken(request.Headers.Authorization!.Parameter);
        token.Claims.ShouldContain(claim => claim.Type == ClaimTypes.NameIdentifier && claim.Value == "user-1");
        token.Claims.ShouldContain(claim => claim.Type == ClaimTypes.Role && claim.Value == "Student");
        token.Claims.ShouldContain(claim => claim.Type == "securityStamp" && claim.Value == "stamp-1");
        token.Claims.ShouldContain(claim => claim.Type == "impersonatedBy" && claim.Value == "admin-9");
        token.ValidTo.ShouldBeInRange(DateTime.UtcNow.AddMinutes(13), DateTime.UtcNow.AddMinutes(17));
    }

    [Fact]
    public async Task Send_AnonymousUser_SendsNoAuthorizationHeader()
    {
        var request = await SendAsHandlerAsync(new ClaimsPrincipal(new ClaimsIdentity()));

        request.Headers.Authorization.ShouldBeNull();
    }

    [Fact]
    public async Task Send_NoHttpContext_SendsNoAuthorizationHeader()
    {
        var request = await SendAsHandlerAsync(user: null);

        request.Headers.Authorization.ShouldBeNull();
    }
}

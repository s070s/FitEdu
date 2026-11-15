using System.Globalization;
using Bunit;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.Common;
using FitEdu.Application.DTOs;
using FitEdu.Shared.Resources;
using FitEdu.Shared.Resources.Messages;
using FitEdu.TestSupport;
using FitEdu.Web.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// The two pages the links in account emails open: /confirm-email (the member's click, not the page
/// load, confirms the address) and /reset-password (a new password for the emailed link).
/// </summary>
public class EmailLinkPagesTests : BunitContext
{
    private const string ConfirmEmailUrl = "/confirm-email?userId=user-1&token=a%2Bb%2Fc%3D";
    private const string ResetPasswordUrl = "/reset-password?email=maria%40fitedu.local&token=a%2Bb%2Fc%3D";
    private const string ValidPassword = "Password1";

    // The tests switch culture; Dispose puts the runner's cultures back.
    private readonly CultureScope _cultureScope = new();

    private readonly IAuthService _authService = Substitute.For<IAuthService>();
    private readonly IAuthApiService _authApiService = Substitute.For<IAuthApiService>();

    public EmailLinkPagesTests()
    {
        CultureInfo.CurrentCulture = new CultureInfo("en");
        CultureInfo.CurrentUICulture = new CultureInfo("en");

        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build()); // _Imports.razor injects it everywhere
        Services.AddSingleton(_authService);
        Services.AddSingleton(_authApiService);
    }

    private void Open(string url) => Services.GetRequiredService<NavigationManager>().NavigateTo(url);

    // ---- /confirm-email -----------------------------------------------------------------------

    [Fact]
    public void ConfirmEmail_OnLoad_OffersTheButtonAndDoesNotUseUpTheToken()
    {
        Open(ConfirmEmailUrl);

        var page = Render<ConfirmEmail>();

        page.Find(".auth-card h1").TextContent.ShouldBe(GlobalRes.ConfirmEmailTitle);
        page.Find("button.auth-submit").TextContent.Trim().ShouldBe(GlobalRes.Label_ConfirmMyEmail);
        // A mail scanner that opens the link must not spend the token: nothing is sent until the click.
        _ = _authApiService.DidNotReceiveWithAnyArgs().ConfirmEmailAsync(default!, default!);
    }

    [Fact]
    public void ConfirmEmail_Click_ConfirmsTheUserIdAndTokenInProcess()
    {
        Open(ConfirmEmailUrl);
        var page = Render<ConfirmEmail>();

        page.Find("button.auth-submit").Click();

        page.WaitForAssertion(() => _ = _authApiService.Received(1).ConfirmEmailAsync("user-1", "a+b/c="));
    }

    [Fact]
    public void ConfirmEmail_Success_ShowsTheResultAndALinkToSignIn()
    {
        _authApiService.ConfirmEmailAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(ServiceResult<AuthResponseDto>.Ok(new AuthResponseDto { Success = true }));
        Open(ConfirmEmailUrl);
        var page = Render<ConfirmEmail>();

        page.Find("button.auth-submit").Click();

        page.WaitForAssertion(() => page.Find(".alert-success").TextContent.ShouldContain(SuccessMessagesRes.EmailConfirmedSuccessfully));
        page.Find("a.auth-submit").GetAttribute("href").ShouldBe("/login");
        page.FindAll("button.auth-submit").ShouldBeEmpty();
    }

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    public void ConfirmEmail_ExpiredOrInvalidLink_ShowsTheFailureWithALinkToSignIn(int statusCode)
    {
        _authApiService.ConfirmEmailAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(new ServiceResult<AuthResponseDto>(new AuthResponseDto { Success = false }, statusCode));
        Open(ConfirmEmailUrl);
        var page = Render<ConfirmEmail>();

        page.Find("button.auth-submit").Click();

        page.WaitForAssertion(() => page.Find(".alert-danger").TextContent.ShouldContain(ErrorMessagesRes.EmailConfirmationLinkInvalid));
        page.Find(".auth-card__footer a").GetAttribute("href").ShouldBe("/login");
        page.FindAll("button.auth-submit").ShouldBeEmpty();
    }

    [Theory]
    [InlineData("/confirm-email")]
    [InlineData(ConfirmEmailUrl)]
    public void ConfirmEmail_InvalidLink_OffersTheWayForward(string url)
    {
        // A link that no longer works must not be a dead end: the sign-in page has the resend button.
        _authApiService.ConfirmEmailAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(new ServiceResult<AuthResponseDto>(new AuthResponseDto { Success = false }, 400));
        Open(url);
        var page = Render<ConfirmEmail>();
        if (url == ConfirmEmailUrl)
            page.Find("button.auth-submit").Click();

        page.WaitForAssertion(() => page.Find(".alert-danger").TextContent.ShouldContain(ErrorMessagesRes.EmailConfirmationLinkInvalid));
        page.Find(".auth-card").TextContent.ShouldContain(GlobalRes.ConfirmEmailLinkInvalidHint);
        var loginLink = page.Find("a.auth-submit");
        loginLink.GetAttribute("href").ShouldBe("/login");
        loginLink.TextContent.Trim().ShouldBe(GlobalRes.Label_GoToLogin);
    }

    [Fact]
    public void ConfirmEmail_ServiceThrows_ShowsATryAgainMessageAndKeepsTheButton()
    {
        _authApiService.ConfirmEmailAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns<ServiceResult<AuthResponseDto>>(_ => throw new InvalidOperationException("boom"));
        Open(ConfirmEmailUrl);
        var page = Render<ConfirmEmail>();

        page.Find("button.auth-submit").Click();

        page.WaitForAssertion(() => page.Find(".alert-danger").TextContent.ShouldContain(ErrorMessagesRes.EmailConfirmationError));
        page.FindAll("button.auth-submit").Count.ShouldBe(1);
    }

    [Fact]
    public void ConfirmEmail_UnexpectedAnswer_ShowsATryAgainMessageAndKeepsTheButton()
    {
        _authApiService.ConfirmEmailAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(new ServiceResult<AuthResponseDto>(new AuthResponseDto { Success = false }, 500));
        Open(ConfirmEmailUrl);
        var page = Render<ConfirmEmail>();

        page.Find("button.auth-submit").Click();

        page.WaitForAssertion(() => page.Find(".alert-danger").TextContent.ShouldContain(ErrorMessagesRes.EmailConfirmationError));
        page.FindAll("button.auth-submit").Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("/confirm-email")]
    [InlineData("/confirm-email?userId=user-1")]
    [InlineData("/confirm-email?token=abc")]
    public void ConfirmEmail_LinkWithoutBothValues_ShowsTheInvalidLinkWithoutAButton(string url)
    {
        Open(url);

        var page = Render<ConfirmEmail>();

        page.Find(".alert-danger").TextContent.ShouldContain(ErrorMessagesRes.EmailConfirmationLinkInvalid);
        page.FindAll("button.auth-submit").ShouldBeEmpty();
        _ = _authApiService.DidNotReceiveWithAnyArgs().ConfirmEmailAsync(default!, default!);
    }

    [Fact]
    public void ConfirmEmail_InGreek_ShowsTheGreekTitleAndButton()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("el-GR");
        Open(ConfirmEmailUrl);

        var page = Render<ConfirmEmail>();

        page.Find(".auth-card h1").TextContent.ShouldBe("Επιβεβαίωση του email σου");
        page.Find("button.auth-submit").TextContent.Trim().ShouldBe("Επιβεβαίωση του email μου");
    }

    // ---- /reset-password ----------------------------------------------------------------------

    private static void FillPasswords(IRenderedComponent<ResetPassword> page, string password, string confirmation)
    {
        page.Find("#newPassword").Change(password);
        page.Find("#confirmPassword").Change(confirmation);
    }

    [Fact]
    public void ResetPassword_OnLoad_ShowsTheNewPasswordFormAndSendsNothing()
    {
        Open(ResetPasswordUrl);

        var page = Render<ResetPassword>();

        page.Find(".auth-card h1").TextContent.ShouldBe(GlobalRes.ResetPasswordTitle);
        page.Find("#newPassword").GetAttribute("autocomplete").ShouldBe("new-password");
        page.Find("#confirmPassword").GetAttribute("type").ShouldBe("password");
        page.Find("button[type=submit]").TextContent.Trim().ShouldBe(GlobalRes.Label_SetNewPassword);
        _ = _authService.DidNotReceiveWithAnyArgs().ResetPasswordAsync(default!);
    }

    [Fact]
    public void ResetPassword_Submit_SendsTheEmailTokenAndNewPasswordAndShowsTheSuccess()
    {
        _authService.ResetPasswordAsync(Arg.Any<ResetPasswordRequestDto>())
            .Returns(new AuthResponseDto { Success = true, Message = SuccessMessagesRes.PasswordResetSuccessful });
        Open(ResetPasswordUrl);
        var page = Render<ResetPassword>();

        FillPasswords(page, ValidPassword, ValidPassword);
        page.Find("form").Submit();

        _ = _authService.Received(1).ResetPasswordAsync(Arg.Is<ResetPasswordRequestDto>(request =>
            request.Email == "maria@fitedu.local" && request.Token == "a+b/c=" &&
            request.NewPassword == ValidPassword && request.ConfirmPassword == ValidPassword));
        page.Find(".alert-success").TextContent.ShouldContain(SuccessMessagesRes.PasswordResetSuccessful);
        page.Find("a.auth-submit").GetAttribute("href").ShouldBe("/login");
        page.FindAll("form").ShouldBeEmpty();
    }

    [Fact]
    public void ResetPassword_PasswordRulesBroken_ShowsEachRuleAndKeepsTheForm()
    {
        _authService.ResetPasswordAsync(Arg.Any<ResetPasswordRequestDto>())
            .Returns(new AuthResponseDto { Success = false, Errors = ["Too common.", "Needs a digit."] });
        Open(ResetPasswordUrl);
        var page = Render<ResetPassword>();

        FillPasswords(page, ValidPassword, ValidPassword);
        page.Find("form").Submit();

        var errors = page.FindAll(".alert-danger li").Select(item => item.TextContent).ToList();
        errors.ShouldBe(["Too common.", "Needs a digit."]);
        page.FindAll("form").Count.ShouldBe(1);
    }

    [Fact]
    public void ResetPassword_ExpiredOrInvalidLink_ShowsTheFailureWithALinkToRequestANewOne()
    {
        _authService.ResetPasswordAsync(Arg.Any<ResetPasswordRequestDto>())
            .Returns(new AuthResponseDto { Success = false, Message = ErrorMessagesRes.InvalidRequest, Failure = AuthFailure.LinkInvalid });
        Open(ResetPasswordUrl);
        var page = Render<ResetPassword>();

        FillPasswords(page, ValidPassword, ValidPassword);
        page.Find("form").Submit();

        page.Find(".alert-danger").TextContent.ShouldContain(ErrorMessagesRes.PasswordResetLinkInvalid);
        page.Find("a.auth-submit").GetAttribute("href").ShouldBe("/forgot-password");
        page.FindAll("form").ShouldBeEmpty();
    }

    [Fact]
    public void ResetPassword_LinkInvalidFailure_WithOtherText_ShowsInvalidLink()
    {
        // The page reads the reason, not the wording, so a reworded or translated message changes nothing.
        _authService.ResetPasswordAsync(Arg.Any<ResetPasswordRequestDto>())
            .Returns(new AuthResponseDto { Success = false, Message = "Some other wording.", Failure = AuthFailure.LinkInvalid });
        Open(ResetPasswordUrl);
        var page = Render<ResetPassword>();

        FillPasswords(page, ValidPassword, ValidPassword);
        page.Find("form").Submit();

        page.Find(".alert-danger").TextContent.ShouldContain(ErrorMessagesRes.PasswordResetLinkInvalid);
        page.FindAll("form").ShouldBeEmpty();
    }

    [Fact]
    public void ResetPassword_SameTextWithoutTheFailure_IsNotTakenForAnInvalidLink()
    {
        _authService.ResetPasswordAsync(Arg.Any<ResetPasswordRequestDto>())
            .Returns(new AuthResponseDto { Success = false, Message = ErrorMessagesRes.InvalidRequest });
        Open(ResetPasswordUrl);
        var page = Render<ResetPassword>();

        FillPasswords(page, ValidPassword, ValidPassword);
        page.Find("form").Submit();

        page.FindAll("form").Count.ShouldBe(1);
        page.Find(".alert-danger").TextContent.ShouldContain(ErrorMessagesRes.InvalidRequest);
    }

    [Fact]
    public void ResetPassword_PasswordsDoNotMatch_ShowsAMessageAndSendsNothing()
    {
        Open(ResetPasswordUrl);
        var page = Render<ResetPassword>();

        FillPasswords(page, ValidPassword, "Different1");
        page.Find("form").Submit();

        page.Find(".validation-message").TextContent.ShouldBe(ErrorMessagesRes.Auth_PasswordMismatch);
        _ = _authService.DidNotReceiveWithAnyArgs().ResetPasswordAsync(default!);
    }

    [Theory]
    [InlineData("/reset-password")]
    [InlineData("/reset-password?email=maria%40fitedu.local")]
    [InlineData("/reset-password?token=abc")]
    public void ResetPassword_LinkWithoutBothValues_ShowsTheInvalidLinkWithoutAForm(string url)
    {
        Open(url);

        var page = Render<ResetPassword>();

        page.Find(".alert-danger").TextContent.ShouldContain(ErrorMessagesRes.PasswordResetLinkInvalid);
        page.Find("a.auth-submit").GetAttribute("href").ShouldBe("/forgot-password");
        page.FindAll("form").ShouldBeEmpty();
    }

    [Fact]
    public void ResetPassword_ServiceThrows_ShowsATryAgainMessage()
    {
        _authService.ResetPasswordAsync(Arg.Any<ResetPasswordRequestDto>())
            .Returns<AuthResponseDto>(_ => throw new InvalidOperationException("boom"));
        Open(ResetPasswordUrl);
        var page = Render<ResetPassword>();

        FillPasswords(page, ValidPassword, ValidPassword);
        page.Find("form").Submit();

        page.Find(".alert-danger").TextContent.ShouldContain(ErrorMessagesRes.UnexpectedErrorTryAgain);
        page.FindAll("form").Count.ShouldBe(1);
    }

    [Fact]
    public void ResetPassword_InGreek_ShowsTheGreekTitle()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("el-GR");
        Open(ResetPasswordUrl);

        var page = Render<ResetPassword>();

        page.Find(".auth-card h1").TextContent.ShouldBe("Όρισε νέο κωδικό πρόσβασης");
    }
}

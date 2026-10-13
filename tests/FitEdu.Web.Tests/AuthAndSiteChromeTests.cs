using System.Globalization;
using Bunit;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Enums;
using FitEdu.Shared.Resources;
using FitEdu.Shared.Resources.Messages;
using FitEdu.Web.Pages;
using FitEdu.Web.Shared.Components.Data;
using FitEdu.Web.Shared.Components.Layout;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// The auth pages (shared AuthCard frame, the register training level and goal), the legal pages,
/// the site footer and the admin table wrapper. Keeps the selectors the e2e suite relies on.
/// </summary>
public class AuthAndSiteChromeTests : BunitContext
{
    private const string ValidPassword = "Password1";

    private readonly IAuthService _authService = Substitute.For<IAuthService>();

    public AuthAndSiteChromeTests()
    {
        CultureInfo.CurrentCulture = new CultureInfo("en");
        CultureInfo.CurrentUICulture = new CultureInfo("en");

        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns("Production");

        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build()); // _Imports.razor injects it everywhere
        Services.AddSingleton(_authService);
        Services.AddSingleton(environment);
        // Register scrolls its result into view after a submit.
        JSInterop.SetupVoid("inputInterop.scrollIntoView", _ => true);
        Services.AddSingleton(Substitute.For<IHttpClientFactory>());
        Services.AddSingleton(new HttpClient());
    }

    // ---- Login / Register ---------------------------------------------------------------------

    [Fact]
    public void Login_RendersTheAuthCardWithTheE2eSelectors()
    {
        var page = Render<Login>();

        page.Find(".auth-card h1").TextContent.ShouldBe(GlobalRes.Label_Login);
        page.FindAll("#email").Count.ShouldBe(1);
        page.FindAll("#password").Count.ShouldBe(1);
        page.FindAll("button[type=submit]").Count.ShouldBe(1);
        page.Find(".auth-card__footer a").GetAttribute("href").ShouldBe("/register");
    }

    [Fact]
    public void Register_OffersTrainingLevelsAndAnOptionalGoal()
    {
        var page = Render<Register>();

        page.FindAll("#status option").Count.ShouldBe(4);
        var goalOptions = page.FindAll("#goal option");
        goalOptions.Count.ShouldBe(Enum.GetValues<FitnessGoal>().Length + 1);
        goalOptions[0].GetAttribute("value").ShouldBe(string.Empty);
        goalOptions[0].TextContent.ShouldBe(GlobalRes.GoalNotSureYet);
        page.Find("label[for=goal]").TextContent.ShouldBe(GlobalRes.Label_Info_Goal);
    }

    [Fact]
    public void Register_KeepsTheConsentLinksTheE2eSuiteChecks()
    {
        var page = Render<Register>();

        page.FindAll(".form-check .form-text a[href=\"/privacy\"]").Count.ShouldBe(1);
        page.FindAll(".form-check .form-text a[href=\"/terms\"]").Count.ShouldBe(1);
        page.FindAll("button[type=submit]").Count.ShouldBe(1);
    }

    [Fact]
    public void Register_Submit_SendsTheChosenLevelAndGoal()
    {
        _authService.RegisterAsync(Arg.Any<RegisterRequestDto>()).Returns(new AuthResponseDto { Success = true });
        var page = Render<Register>();

        FillRequiredRegisterFields(page);
        page.Find("#status").Change(nameof(UserStatus.Advanced));
        page.Find("#goal").Change(nameof(FitnessGoal.Endurance));
        page.Find("form").Submit();

        _authService.Received(1).RegisterAsync(Arg.Is<RegisterRequestDto>(request =>
            request.Status == UserStatus.Advanced && request.Goal == FitnessGoal.Endurance));
    }

    [Fact]
    public void Register_SubmitWithoutGoal_SendsNoGoal()
    {
        _authService.RegisterAsync(Arg.Any<RegisterRequestDto>()).Returns(new AuthResponseDto { Success = true });
        var page = Render<Register>();

        FillRequiredRegisterFields(page);
        page.Find("form").Submit();

        _authService.Received(1).RegisterAsync(Arg.Is<RegisterRequestDto>(request => request.Goal == null));
    }

    [Fact]
    public void Register_Success_ReplacesTheFormWithTheResultAndALoginLink()
    {
        // On a phone the alert above a long form was out of sight; the result now takes the form's place.
        _authService.RegisterAsync(Arg.Any<RegisterRequestDto>()).Returns(new AuthResponseDto { Success = true });
        var page = Render<Register>();

        FillRequiredRegisterFields(page);
        page.Find("form").Submit();

        page.FindAll("form").ShouldBeEmpty();
        page.Find(".register-success .alert-success").TextContent.ShouldContain(SuccessMessagesRes.RegistrationSuccess);
        page.Find(".register-success__login").GetAttribute("href").ShouldBe("/login");
        JSInterop.VerifyInvoke("inputInterop.scrollIntoView");
    }

    [Fact]
    public void Register_ServerError_ShowsAnAlertRightAboveTheSubmitButton()
    {
        _authService.RegisterAsync(Arg.Any<RegisterRequestDto>()).Returns(
            new AuthResponseDto { Success = false, Message = "failed", Errors = ["Email 'maria@fitedu.local' is already taken."] });
        var page = Render<Register>();

        FillRequiredRegisterFields(page);
        page.Find("form").Submit();

        var feedback = page.Find("form .register-errors");
        feedback.GetAttribute("role").ShouldBe("alert");
        feedback.TextContent.ShouldContain(ErrorMessagesRes.EmailTakenError);
        feedback.TextContent.ShouldNotContain("already taken");
        feedback.NextElementSibling!.GetAttribute("type").ShouldBe("submit");
        // The phone user tapped at the bottom edge, so the alert is scrolled into view.
        JSInterop.VerifyInvoke("inputInterop.scrollIntoView");
    }

    [Fact]
    public void ForgotPassword_OutsideDevelopment_HidesTheDirectReset()
    {
        _authService.ForgotPasswordAsync(Arg.Any<ForgotPasswordRequestDto>()).Returns(new AuthResponseDto { Success = true, Message = "sent" });
        var page = Render<ForgotPassword>();

        page.Find("#email").Change("member@fitedu.local");
        page.Find("form").Submit();

        page.Find(".alert-success").TextContent.ShouldContain("sent");
        page.FindAll(".alert-success input[type=password]").ShouldBeEmpty();
    }

    [Fact]
    public void ForgotPassword_AfterTheLinkIsSent_DropsTheFormInstructions()
    {
        _authService.ForgotPasswordAsync(Arg.Any<ForgotPasswordRequestDto>()).Returns(new AuthResponseDto { Success = true, Message = "sent" });
        var page = Render<ForgotPassword>();
        page.Find(".auth-card__subtitle").TextContent.ShouldBe(GlobalRes.ForgotPasswordInstructions);

        page.Find("#email").Change("member@fitedu.local");
        page.Find("form").Submit();

        page.FindAll(".auth-card__subtitle").ShouldBeEmpty();
    }

    // ---- Legal pages --------------------------------------------------------------------------

    [Fact]
    public void Privacy_RendersNumberedSectionsAndLinksTheTerms()
    {
        var page = Render<Privacy>();

        page.Find(".legal-content h1").TextContent.ShouldBe(LegalRes.PrivacyTitle);
        page.FindAll(".legal-sections > li h2").Count.ShouldBe(7);
        page.Find(".legal-footer a").GetAttribute("href").ShouldBe("/terms");
    }

    [Fact]
    public void Terms_RendersNumberedSectionsAndLinksThePrivacyPolicy()
    {
        var page = Render<Terms>();

        page.Find(".legal-content h1").TextContent.ShouldBe(LegalRes.TermsTitle);
        page.FindAll(".legal-sections > li h2").Count.ShouldBe(7);
        page.Find(".legal-footer a").GetAttribute("href").ShouldBe("/privacy");
    }

    // ---- Footer and admin tables --------------------------------------------------------------

    [Fact]
    public void SiteFooter_ListsExploreAndLegalLinksWithIconFonts()
    {
        var footer = Render<SiteFooter>();

        var hrefs = footer.FindAll(".site-footer__links a").Select(link => link.GetAttribute("href")).ToList();
        hrefs.ShouldBe(["/courses", "/exercises", "/paths", "/pricing", "/blog", "/privacy", "/terms"]);
        footer.FindAll(".social-link .bi").Count.ShouldBe(2);
        footer.FindAll("svg:not(.brand-mark)").ShouldBeEmpty();
    }

    [Fact]
    public void ScrollableTable_PassesTheHeightAsAVariableAndNamesTheTable()
    {
        var table = Render<ScrollableTable<string>>(parameters => parameters
            .Add(component => component.Items, ["one", "two"])
            .Add(component => component.HeaderRow, "<tr><th>Name</th></tr>")
            .Add(component => component.RowTemplate, item => $"<tr><td data-label=\"Name\">{item}</td></tr>")
            .Add(component => component.MaxHeight, "50vh")
            .Add(component => component.Caption, "Users"));

        var wrapper = table.Find(".table-responsive");
        var style = wrapper.GetAttribute("style") ?? string.Empty;
        style.ShouldContain("--table-max-height:50vh");
        style.ShouldNotContain("overflow");
        table.FindAll("tbody tr").Count.ShouldBe(2);
        table.Find("caption.visually-hidden").TextContent.ShouldBe("Users"); // the table's accessible name
    }

    private static void FillRequiredRegisterFields(IRenderedComponent<Register> page)
    {
        page.Find("#firstName").Change("Maria");
        page.Find("#lastName").Change("Papadopoulou");
        page.Find("#email").Change("maria@fitedu.local");
        page.Find("#password").Change(ValidPassword);
        page.Find("#confirmPassword").Change(ValidPassword);
        page.Find("#gdprConsent").Change(true);
    }
}

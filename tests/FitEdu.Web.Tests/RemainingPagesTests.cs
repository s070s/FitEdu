using System.Globalization;
using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Enums;
using FitEdu.Shared.Resources;
using FitEdu.Shared.Resources.Messages;
using FitEdu.Web.Consumers;
using FitEdu.Web.Extensions;
using FitEdu.Web.Pages;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// bUnit tests for the D13 page sweep: the single-message pages use the status panel, the analytics
/// page shows stat tiles and completion bars, and the course editor's pickers show translated labels.
/// </summary>
public class RemainingPagesTests : BunitContext
{
    private static readonly Guid ValidId = Guid.NewGuid();
    private static readonly Guid RevokedId = Guid.NewGuid();
    private static readonly Guid UnreachableId = Guid.NewGuid();

    private readonly ICertificateConsumer _certificateConsumer = Substitute.For<ICertificateConsumer>();
    private readonly IAdminAnalyticsConsumer _analyticsConsumer = Substitute.For<IAdminAnalyticsConsumer>();

    public RemainingPagesTests()
    {
        CultureInfo.CurrentCulture = new CultureInfo("en-US");
        CultureInfo.CurrentUICulture = new CultureInfo("en");
        Services.AddSingleton(_certificateConsumer);
        Services.AddSingleton(_analyticsConsumer);
        // _Imports.razor injects IConfiguration into every page.
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        _certificateConsumer.VerifyAsync(ValidId, Arg.Any<string>()).Returns(
            new CertificateVerificationDto(true, "Alice Johnson", "Strength Foundations", new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc), "Active", null));
        _certificateConsumer.VerifyAsync(RevokedId, Arg.Any<string>()).Returns(
            new CertificateVerificationDto(false, null, null, null, "Revoked", "This certificate was revoked."));
        _certificateConsumer.VerifyAsync(UnreachableId, Arg.Any<string>()).Returns((CertificateVerificationDto?)null);
    }

    [Fact]
    public void NotFound_ShowsHeadingAndHomeLinkInStatusPanel()
    {
        var page = Render<NotFound>();

        page.Find(".status-panel h1").TextContent.ShouldBe(GlobalRes.NotFoundTitle);
        page.Find(".status-panel__actions a[href='/']").TextContent.ShouldBe(GlobalRes.BackToHome);
    }

    [Fact]
    public void Disabled_UsesDangerToneAndLinksBackToLogin()
    {
        var page = Render<Disabled>();

        page.Find(".status-panel").ClassList.ShouldContain("status-panel--danger");
        page.Find(".status-panel h1").TextContent.ShouldBe(ErrorMessagesRes.AccountDisabled);
        page.Find("a[href='/login']").TextContent.ShouldBe(ErrorMessagesRes.BackToLogin);
    }

    [Fact]
    public void VerifyCertificate_ValidCertificate_ListsRecipientAndCourse()
    {
        var page = Render<VerifyCertificate>(parameters => parameters.Add(p => p.VerificationId, ValidId));

        page.Find(".status-panel").ClassList.ShouldContain("status-panel--success");
        page.Find(".status-panel h1").TextContent.ShouldBe(CertificateRes.VerificationValid);
        var values = page.FindAll(".certificate-details dd").Select(value => value.TextContent).ToList();
        values.ShouldContain("Alice Johnson");
        values.ShouldContain("Strength Foundations");
    }

    [Fact]
    public void VerifyCertificate_RevokedCertificate_ShowsServerMessage()
    {
        var page = Render<VerifyCertificate>(parameters => parameters.Add(p => p.VerificationId, RevokedId));

        page.Find(".status-panel").ClassList.ShouldContain("status-panel--danger");
        page.Find(".status-panel h1").TextContent.ShouldBe(CertificateRes.VerificationInvalid);
        page.Find(".status-panel__lead").TextContent.ShouldBe("This certificate was revoked.");
    }

    [Fact]
    public void VerifyCertificate_ServiceUnreachable_ShowsUnreachableMessage()
    {
        var page = Render<VerifyCertificate>(parameters => parameters.Add(p => p.VerificationId, UnreachableId));

        page.Find(".status-panel__lead").TextContent.ShouldBe(CertificateRes.VerificationUnreachable);
        page.FindAll(".certificate-details").ShouldBeEmpty();
    }

    [Fact]
    public void AdminAnalytics_ShowsFourTilesAndCompletionBars()
    {
        _analyticsConsumer.GetSummaryAsync().Returns(new AnalyticsSummaryDto(1240, 873, 2310, 418,
            [new("Strength Foundations", 640, 212), new("Draft course", 0, 0)],
            [new("Training readiness", 380)]));

        var page = Render<AdminAnalytics>();

        page.FindAll(".stat-tile").Count.ShouldBe(4);
        page.FindAll(".stat-tile__value")[0].TextContent.ShouldBe("1,240");
        page.Find(".completion-rate__bar").GetAttribute("value").ShouldBe("33");
        page.Find(".completion-rate").TextContent.ShouldContain("33%");
        page.FindAll(".completion-rate").Count.ShouldBe(1, "a course nobody enrolled in shows a dash, not a bar");
    }
}

/// <summary>The course editor's level and goal pickers show translated labels but still post the enum names.</summary>
public class AdminCourseEditLabelTests : BunitContext
{
    public AdminCourseEditLabelTests()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("el");
        Services.AddSingleton(Substitute.For<IAdminCourseConsumer>());
        Services.AddSingleton(Substitute.For<IAdminModuleConsumer>());
        Services.AddSingleton(Substitute.For<IAdminLessonConsumer>());
        Services.AddSingleton(Substitute.For<IExerciseConsumer>());
        var categoryConsumer = Substitute.For<IAdminCategoryConsumer>();
        categoryConsumer.GetAllCategoriesAsync().Returns([]);
        Services.AddSingleton(categoryConsumer);
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void LevelAndGoalOptions_ShowTranslatedLabels()
    {
        var page = Render<AdminCourseEdit>(parameters => parameters.Add(p => p.CourseId, Guid.Empty));

        var levelOption = page.Find($"#course-level option[value='{nameof(CourseLevel.Intermediate)}']");
        levelOption.TextContent.ShouldBe(CourseLabels.Level(CourseLevel.Intermediate));
        levelOption.TextContent.ShouldNotBe(nameof(CourseLevel.Intermediate));

        var goalOption = page.Find($"#course-goal option[value='{nameof(FitnessGoal.MuscleGain)}']");
        goalOption.TextContent.ShouldBe(CourseLabels.Goal(FitnessGoal.MuscleGain));
    }
}

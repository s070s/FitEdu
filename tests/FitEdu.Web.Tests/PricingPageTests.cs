using System.Globalization;
using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Enums;
using FitEdu.Shared.Resources;
using FitEdu.TestSupport;
using FitEdu.Web.Consumers;
using FitEdu.Web.Pages;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// bUnit tests for the pricing page: the plans are asked for in the reader's language, prices are
/// euros in the reader's number format, and each card lists its own course access line.
/// </summary>
public class PricingPageTests : BunitContext
{
    // The tests switch culture; Dispose puts the runner's cultures back.
    private readonly CultureScope _cultureScope = new();

    private readonly ISubscriptionConsumer _subscriptionConsumer = Substitute.For<ISubscriptionConsumer>();

    public PricingPageTests()
    {
        Services.AddSingleton(_subscriptionConsumer);
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        AddAuthorization();

        _subscriptionConsumer.GetPlansAsync(Arg.Any<string>()).Returns(
        [
            Plan("Free", 0m, SubscriptionTier.Free, maxCourses: 1, assessments: false, certificates: false, prioritySupport: false),
            Plan("Plus", 9.99m, SubscriptionTier.Plus, maxCourses: 10, assessments: true, certificates: false, prioritySupport: false),
            Plan("Pro", 19.99m, SubscriptionTier.Pro, maxCourses: int.MaxValue, assessments: true, certificates: true, prioritySupport: true)
        ]);
    }

    private static SubscriptionPlanDto Plan(
        string name, decimal price, SubscriptionTier tier, int maxCourses, bool assessments, bool certificates, bool prioritySupport) =>
        new(Guid.NewGuid(), name, $"{name} description", price, nameof(BillingPeriod.Monthly), tier,
            new PlanFeaturesDto
            {
                MaxCourses = maxCourses,
                AssessmentAccess = assessments,
                CertificateAccess = certificates,
                PrioritySupport = prioritySupport
            },
            IsActive: true);

    private static void UseCulture(string name)
    {
        CultureInfo.CurrentCulture = new CultureInfo(name);
        CultureInfo.CurrentUICulture = new CultureInfo(name);
    }

    [Fact]
    public void GreekReader_AsksForGreekPlansAndSeesGreekEuroPrices()
    {
        UseCulture("el-GR");

        var page = Render<Pricing>();

        page.WaitForAssertion(() => page.FindAll(".plan-card").Count.ShouldBe(3));
        _subscriptionConsumer.Received().GetPlansAsync("el");
        var prices = page.FindAll(".price-amount").Select(price => price.TextContent.Replace(' ', ' ')).ToList();
        prices.ShouldContain("9,99 €");
        page.Markup.ShouldContain(PricingRes.PeriodMonthly);
    }

    [Fact]
    public void EnglishReader_SeesEuroNotPounds()
    {
        UseCulture("en-GB");

        var page = Render<Pricing>();

        page.WaitForAssertion(() => page.FindAll(".price-amount").Select(price => price.TextContent).ShouldContain("€19.99"));
        _subscriptionConsumer.Received().GetPlansAsync("en");
        page.Markup.ShouldNotContain("£");
    }

    [Fact]
    public void EachCard_ListsItsOwnCourseAccessAndTheSharedRows()
    {
        UseCulture("en-GB");

        var page = Render<Pricing>();

        page.WaitForAssertion(() => page.FindAll(".plan-card").Count.ShouldBe(3));
        var firstRows = page.FindAll(".plan-card .plan-features")
            .Select(list => list.QuerySelector(".feature-item")!.TextContent.Trim())
            .ToList();
        firstRows.ShouldBe([PricingRes.FeatureCoursesFree, PricingRes.FeatureCoursesPlus, PricingRes.FeatureCoursesPro]);
        page.FindAll(".plan-features").ShouldAllBe(list =>
            list.TextContent.Contains(PricingRes.FeatureExerciseLibrary) && list.TextContent.Contains(PricingRes.FeatureLiveClasses));
    }

    [Theory]
    [InlineData("en-GB")]
    [InlineData("el-GR")]
    public void FreeCard_NamesItsSingleCourseInTheSingular(string cultureName)
    {
        UseCulture(cultureName);

        var page = Render<Pricing>();

        page.WaitForAssertion(() => page.FindAll(".plan-card").Count.ShouldBe(3));
        var limitRows = page.FindAll(".plan-card .plan-features")
            .Select(list => list.QuerySelectorAll(".feature-item")[1].TextContent.Trim())
            .ToList();
        limitRows.ShouldBe([
            PricingRes.OneCourseAtATime,
            PricingRes.UpToCoursesFormat.Replace("{0}", "10", StringComparison.Ordinal),
            PricingRes.UnlimitedCourses]);
    }

    [Fact]
    public void FreeCard_ShowsAZeroPriceInsteadOfRepeatingItsName()
    {
        UseCulture("en-GB");

        var page = Render<Pricing>();

        page.WaitForAssertion(() => page.FindAll(".plan-card").Count.ShouldBe(3));
        var freeCard = page.FindAll(".plan-card").Single(card => card.QuerySelector(".plan-name")!.TextContent == "Free");
        freeCard.QuerySelector(".price-amount")!.TextContent.ShouldBe("€0.00");
        freeCard.QuerySelector(".price-period")!.TextContent.ShouldBe(PricingRes.PeriodForever);
    }

    [Fact]
    public void MissingFeature_IsCrossedOutWithHiddenNotIncludedText()
    {
        UseCulture("en-GB");

        var page = Render<Pricing>();

        page.WaitForAssertion(() => page.FindAll(".plan-card").Count.ShouldBe(3));
        var freeCard = page.FindAll(".plan-card").Single(card => card.QuerySelector(".plan-name")!.TextContent == "Free");
        var disabledRows = freeCard.QuerySelectorAll(".feature-disabled");
        disabledRows.Length.ShouldBe(3);
        disabledRows.ShouldAllBe(row => row.QuerySelector(".visually-hidden")!.TextContent == PricingRes.FeatureNotIncluded);
    }

    [Fact]
    public void ProPlan_IsTheFeaturedCard()
    {
        UseCulture("en-GB");

        var page = Render<Pricing>();

        page.WaitForAssertion(() => page.Find(".plan-featured .plan-name").TextContent.ShouldBe("Pro"));
        page.Find(".plan-featured .plan-badge").TextContent.ShouldContain(PricingRes.MostPopular);
    }

    protected override void Dispose(bool disposing)
    {
        _cultureScope.Dispose();
        base.Dispose(disposing);
    }
}

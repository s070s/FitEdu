using System.Globalization;
using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Entities;
using FitEdu.Domain.Enums;
using FitEdu.Web.Consumers;
using FitEdu.Web.Pages;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// bUnit tests for the billing page's transaction list: the server writes each description in
/// English, so only the English page shows it; the type chip carries the meaning in Greek.
/// </summary>
public class BillingPageTests : BunitContext
{
    private const string EnglishDescription = "Upgraded from Plus to Pro";

    private readonly ISubscriptionConsumer _subscriptionConsumer = Substitute.For<ISubscriptionConsumer>();

    // Each test switches the thread culture, so the originals are put back after it.
    private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _originalUICulture = CultureInfo.CurrentUICulture;

    public BillingPageTests()
    {
        Services.AddSingleton(_subscriptionConsumer);
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        AddAuthorization();

        var upgrade = new BillingTransactionDto(Guid.NewGuid(), "Pro", SubscriptionTier.Pro, 19.99m, "eur",
            nameof(BillingTransactionType.Upgrade), EnglishDescription, StripeSessionId: null, DateTime.UtcNow);
        var overview = new BillingOverviewDto
        {
            CurrentSubscription = new UserSubscriptionStatusDto(SubscriptionTier.Pro, "Pro", DateTime.UtcNow, null, true, null),
            Transactions = new PagedResult<BillingTransactionDto>([upgrade], 1, 1, 10)
        };
        _subscriptionConsumer
            .GetBillingOverviewAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(overview);
    }

    private static void UseCulture(string name)
    {
        CultureInfo.CurrentCulture = new CultureInfo(name);
        CultureInfo.CurrentUICulture = new CultureInfo(name);
    }

    protected override void Dispose(bool disposing)
    {
        CultureInfo.CurrentCulture = _originalCulture;
        CultureInfo.CurrentUICulture = _originalUICulture;
        base.Dispose(disposing);
    }

    [Fact]
    public void EnglishReader_SeesTheTransactionDescription()
    {
        UseCulture("en");

        var page = Render<Billing>();

        page.WaitForAssertion(() => page.FindAll(".tx-type").Count.ShouldBe(1));
        page.Find(".tx-description").TextContent.ShouldBe(EnglishDescription);
    }

    [Fact]
    public void GreekReader_SeesTheTypeChipWithoutTheEnglishDescription()
    {
        UseCulture("el-GR");

        var page = Render<Billing>();

        page.WaitForAssertion(() => page.FindAll(".tx-type").Count.ShouldBe(1));
        page.Find(".tx-type").TextContent.ShouldBe("Αναβάθμιση");
        page.FindAll(".tx-description").ShouldBeEmpty();
        page.Markup.ShouldNotContain(EnglishDescription);
    }
}

using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Enums;
using FitEdu.Shared.Resources;
using FitEdu.Web.Consumers;
using FitEdu.Web.Pages;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// bUnit tests for the member pages restyled in D11: certificate cards, the notifications feed
/// (sort choice reaches the API, unread rows offer mark-read) and the search page form.
/// </summary>
public class MemberPagesTests : BunitContext
{
    private readonly ICertificateConsumer _certificateConsumer = Substitute.For<ICertificateConsumer>();
    private readonly INotificationConsumer _notificationConsumer = Substitute.For<INotificationConsumer>();
    private readonly ISearchConsumer _searchConsumer = Substitute.For<ISearchConsumer>();
    private readonly ISubscriptionConsumer _subscriptionConsumer = Substitute.For<ISubscriptionConsumer>();

    public MemberPagesTests()
    {
        Services.AddSingleton(_certificateConsumer);
        Services.AddSingleton(_notificationConsumer);
        Services.AddSingleton(_searchConsumer);
        Services.AddSingleton(_subscriptionConsumer);
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        AddAuthorization();
    }

    [Fact]
    public void MyCertificates_RendersOneCardPerCertificateWithDownloadAndVerifyLinks()
    {
        var certificate = new CertificateDto(Guid.NewGuid(), Guid.NewGuid(), "Alice Johnson", "Couch to 5K", DateTime.UtcNow, "Active");
        _certificateConsumer.GetMyCertificatesAsync(Arg.Any<string>()).Returns([certificate]);

        var page = Render<MyCertificates>();

        var card = page.Find(".certificate-card");
        card.QuerySelector(".certificate-card__title")!.TextContent.ShouldBe("Couch to 5K");
        card.QuerySelector($"a[href='/api/certificates/{certificate.Id}/download']").ShouldNotBeNull();
        card.QuerySelector($"a[href='/certificate/{certificate.VerificationId}']").ShouldNotBeNull();
    }

    [Fact]
    public void MyCertificates_None_ShowsEmptyStateLinkingToCourses()
    {
        _certificateConsumer.GetMyCertificatesAsync(Arg.Any<string>()).Returns([]);

        var page = Render<MyCertificates>();

        page.Find(".empty-state__action").GetAttribute("href").ShouldBe("/courses");
    }

    [Fact]
    public void MyCertificates_NoneOnAPlanWithoutCertificates_OffersTheProUpgrade()
    {
        _certificateConsumer.GetMyCertificatesAsync(Arg.Any<string>()).Returns([]);
        _subscriptionConsumer.GetStatusAsync().Returns(new UserSubscriptionStatusDto(
            SubscriptionTier.Free, "Free", DateTime.UtcNow, null, false, new PlanFeaturesDto { CertificateAccess = false }));

        var page = Render<MyCertificates>();

        page.Find(".certificates-head__lead").TextContent.ShouldBe(CertificateRes.SubtitleNoAccess);
        page.Find(".upgrade-prompt__action").GetAttribute("href").ShouldBe("/pricing");
        page.FindAll(".empty-state").ShouldBeEmpty();
    }

    [Fact]
    public void Notifications_UnreadRow_IsMarkedAndOffersMarkRead()
    {
        ReturnNotifications(Notification(isRead: false), Notification(isRead: true));

        var page = Render<Notifications>();

        page.FindAll(".notification-row").Count.ShouldBe(2);
        page.FindAll(".notification-row--unread").Count.ShouldBe(1);
        page.FindAll(".notification-row__mark").Count.ShouldBe(1);
    }

    [Fact]
    public void Notifications_UnreadFirstSort_AsksTheApiForIsReadAscending()
    {
        ReturnNotifications(Notification(isRead: false));
        var page = Render<Notifications>();

        page.Find("#notification-sort").Change("unread");

        _notificationConsumer.Received().GetNotificationsAsync(1, Arg.Any<int>(), "isread", "asc");
    }

    [Fact]
    public void Notifications_ShowsAnEyebrowWithTheBellIcon()
    {
        ReturnNotifications(Notification(isRead: false));

        var page = Render<Notifications>();

        var eyebrow = page.Find(".notifications-head__text .eyebrow");
        eyebrow.TextContent.Trim().ShouldBe(NavMenuRes.Notifications);
        eyebrow.QuerySelector(".bi")!.ClassList.ShouldContain("bi-bell");
    }

    [Fact]
    public void Search_ShowsAnEyebrowWithTheSearchIcon()
    {
        var page = Render<Search>();

        var eyebrow = page.Find(".search-head__text .eyebrow");
        eyebrow.TextContent.Trim().ShouldBe(NavMenuRes.Search);
        eyebrow.QuerySelector(".bi")!.ClassList.ShouldContain("bi-search");
    }

    [Fact]
    public void Search_SubmittingTheForm_NavigatesToTheQuery()
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        var page = Render<Search>();

        page.Find("input[type=search]").Input("  squat  ");
        page.Find("form.search-form").Submit();

        navigation.Uri.ShouldEndWith("/search?q=squat");
    }

    private void ReturnNotifications(params NotificationDto[] notifications)
    {
        var result = new PagedResult<NotificationDto>([.. notifications], notifications.Length, Page: 1, PageSize: 10);
        _notificationConsumer.GetNotificationsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(result);
    }

    private static NotificationDto Notification(bool isRead) => new(
        Guid.NewGuid(), "CertificateIssued", nameof(NotificationRes.CertificateIssued), ["Couch to 5K"],
        "/my-certificates", isRead, DateTimeOffset.UtcNow.AddMinutes(-5));
}

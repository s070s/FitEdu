using System.Globalization;
using FitEdu.Application.DTOs;
using FitEdu.Web.Services;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// Course and session notifications store the English title as {0} and the Greek title as {1};
/// each language's template picks its own. Rows stored before the Greek title was added have one argument.
/// </summary>
public class NotificationMessageFormatterTests
{
    private static NotificationDto Notification(string titleKey, params string[] bodyArgs) =>
        new(Guid.NewGuid(), "CertificateIssued", titleKey, [.. bodyArgs], "/my-certificates", false, DateTimeOffset.UtcNow);

    [Fact]
    public void Greek_WithBothTitles_ShowsTheGreekTitle()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("el");

        var text = NotificationMessageFormatter.Format(Notification("CertificateIssued", "Strength Foundations", "Θεμέλια Δύναμης"));

        text.ShouldBe("Το πιστοποιητικό σου για το \"Θεμέλια Δύναμης\" είναι έτοιμο");
    }

    [Theory]
    [InlineData("CertificateIssued")]
    [InlineData("ReviewApproved")]
    [InlineData("SessionReminder")]
    public void Greek_WithOnlyTheEnglishTitle_FallsBackToIt(string titleKey)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("el");

        var text = NotificationMessageFormatter.Format(Notification(titleKey, "Strength Foundations"));

        text.ShouldContain("\"Strength Foundations\"", Case.Sensitive);
        text.ShouldNotContain("{");
    }

    [Fact]
    public void English_WithBothTitles_ShowsTheEnglishTitle()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");

        var text = NotificationMessageFormatter.Format(Notification("ReviewApproved", "Strength Foundations", "Θεμέλια Δύναμης"));

        text.ShouldBe("Your review for \"Strength Foundations\" was approved");
    }

    [Fact]
    public void Greek_SessionReminderWithBothTitles_ShowsTheGreekTitle()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("el");

        var text = NotificationMessageFormatter.Format(Notification("SessionReminder", "Mobility Class", "Μάθημα κινητικότητας"));

        text.ShouldBe("Το \"Μάθημα κινητικότητας\" ξεκινά σε 15 λεπτά");
    }
}

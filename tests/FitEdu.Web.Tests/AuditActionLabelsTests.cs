using System.Globalization;
using FitEdu.Domain.Enums;
using FitEdu.TestSupport;
using FitEdu.Web.Extensions;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

public class AuditActionLabelsTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("el")]
    public void EveryAction_HasALabelInEachLanguage_NotItsEnumName(string culture)
    {
        using var cultureScope = new CultureScope(culture);

        foreach (var action in Enum.GetValues<AdminAuditAction>())
        {
            var label = AuditActionLabels.Label(action);

            label.ShouldNotBeNullOrWhiteSpace();
            label.ShouldNotBe(action.ToString(), $"{action} has no resource text");
        }
    }

    [Fact]
    public void EveryAction_IsLabelledDifferentlyInGreekAndEnglish()
    {
        var english = Enum.GetValues<AdminAuditAction>().ToDictionary(action => action, action => LabelIn("en", action));
        var greek = Enum.GetValues<AdminAuditAction>().ToDictionary(action => action, action => LabelIn("el", action));

        foreach (var action in english.Keys)
            greek[action].ShouldNotBe(english[action], $"{action} has no Greek text");
        english.Values.Distinct().Count().ShouldBe(english.Count, "two actions share an English label");
    }

    [Fact]
    public void AnUnknownAction_IsShownByItsName()
    {
        AuditActionLabels.Label((AdminAuditAction)999).ShouldBe("999");
    }

    private static string LabelIn(string culture, AdminAuditAction action)
    {
        using var cultureScope = new CultureScope(new CultureInfo(culture));
        return AuditActionLabels.Label(action);
    }
}

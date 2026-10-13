using FitEdu.Web.Extensions;
using Shouldly;
using Xunit;
using FitEdu.TestSupport;

namespace FitEdu.Web.Tests;

/// <summary>
/// The admin lists receive a class's status and a user's roles as stored names ("Scheduled",
/// "Student"); they must show translated labels, so a Greek admin never reads raw English values.
/// </summary>
public class AdminLabelsTests
{
    [Theory]
    [InlineData("Scheduled", "Προγραμματισμένο")]
    [InlineData("Live", "Ζωντανά")]
    [InlineData("Ended", "Ολοκληρώθηκε")]
    [InlineData("Cancelled", "Ακυρώθηκε")]
    public void SessionStatus_Greek_ReturnsTheTranslatedLabel(string statusName, string expected)
    {
        using var cultureScope = new CultureScope("el");

        SessionLabels.Status(statusName).ShouldBe(expected);
    }

    [Theory]
    [InlineData("Scheduled", "Scheduled")]
    [InlineData("Cancelled", "Cancelled")]
    [InlineData("NotAStatus", "NotAStatus")]
    public void SessionStatus_English_ReturnsTheLabelOrTheNameItself(string statusName, string expected)
    {
        using var cultureScope = new CultureScope("en");

        SessionLabels.Status(statusName).ShouldBe(expected);
    }

    [Theory]
    [InlineData("Admin", "Διαχειριστής")]
    [InlineData("Student", "Μέλος")]
    [InlineData("Coach", "Coach")]
    public void Role_Greek_ReturnsTheTranslatedLabelOrTheNameItself(string roleName, string expected)
    {
        using var cultureScope = new CultureScope("el");

        RoleLabels.Label(roleName).ShouldBe(expected);
    }

    [Theory]
    [InlineData("Admin", "Admin")]
    [InlineData("Student", "Member")]
    public void Role_English_ReturnsTheMemberFacingName(string roleName, string expected)
    {
        using var cultureScope = new CultureScope("en");

        RoleLabels.Label(roleName).ShouldBe(expected);
    }
}

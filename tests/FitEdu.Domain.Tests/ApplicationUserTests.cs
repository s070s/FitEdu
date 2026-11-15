using FitEdu.Domain.Identity;
using Shouldly;
using Xunit;

namespace FitEdu.Domain.Tests;

/// <summary>
/// <see cref="ApplicationUser.AgeOn"/> is computed from <see cref="ApplicationUser.DateOfBirth"/> against
/// the date the caller passes (the members' today, not the server's UTC date). The date is fixed, so the
/// cases give the same result on every run, including leap days.
/// </summary>
public class ApplicationUserTests
{
    private static readonly DateOnly Today = new(2026, 11, 15);

    private static ApplicationUser WithDob(DateOnly? dob) =>
        new() { FirstName = "Test", LastName = "User", DateOfBirth = dob };

    [Fact]
    public void AgeOn_NullDateOfBirth_ReturnsNull()
    {
        WithDob(null).AgeOn(Today).ShouldBeNull();
    }

    [Fact]
    public void AgeOn_BirthdayIsToday_ReturnsExactYears()
    {
        WithDob(Today.AddYears(-25)).AgeOn(Today).ShouldBe(25);
    }

    [Fact]
    public void AgeOn_BirthdayWasYesterday_ReturnsExactYears()
    {
        WithDob(Today.AddYears(-25).AddDays(-1)).AgeOn(Today).ShouldBe(25);
    }

    [Fact]
    public void AgeOn_BirthdayIsTomorrow_ReturnsYearsMinusOne()
    {
        WithDob(Today.AddYears(-25).AddDays(1)).AgeOn(Today).ShouldBe(24);
    }

    [Fact]
    public void AgeOn_YoungChild_ReturnsYears()
    {
        WithDob(Today.AddYears(-5)).AgeOn(Today).ShouldBe(5);
    }

    [Fact]
    public void AgeOn_FollowsTheDatePassedIn_SoTheDayBeforeTheBirthdayIsStillYoung()
    {
        // The 18th birthday is on 15 June: a member whose own date is still 14 June is 17.
        var dateOfBirth = new DateOnly(2008, 6, 15);

        WithDob(dateOfBirth).AgeOn(new DateOnly(2026, 6, 14)).ShouldBe(17);
        WithDob(dateOfBirth).AgeOn(new DateOnly(2026, 6, 15)).ShouldBe(18);
    }

    [Fact]
    public void AgeOn_LeapDayBirthday_CountsTheBirthdayOnFirstOfMarchInCommonYears()
    {
        var dateOfBirth = new DateOnly(2008, 2, 29);

        WithDob(dateOfBirth).AgeOn(new DateOnly(2026, 2, 28)).ShouldBe(17);
        WithDob(dateOfBirth).AgeOn(new DateOnly(2026, 3, 1)).ShouldBe(18);
    }
}

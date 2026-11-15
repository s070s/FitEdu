using System.Text.RegularExpressions;
using FitEdu.Application.Common;
using FitEdu.Domain.Identity;
using FitEdu.TestSupport;
using Shouldly;
using Xunit;

namespace FitEdu.Application.Tests;

/// <summary>
/// The shared password rules and the common-password check (SEC-8): a local list, no outside service.
/// </summary>
public class CommonPasswordTests
{
    private readonly CommonPasswordValidator _validator = new();

    private static ApplicationUser Member() => new() { FirstName = "F", LastName = "L" };

    [Theory]
    [InlineData("Password123")]
    [InlineData("PASSWORD123")]
    [InlineData("qwerty123")]
    [InlineData("1Q2W3E4R")]
    [InlineData("Welcome1")]
    public void IsCommon_ListedPassword_IgnoresLetterCase(string password)
    {
        CommonPasswords.IsCommon(password).ShouldBeTrue();
    }

    [Theory]
    // Passwords the tests and the seeders use for real Identity accounts must never be on the list.
    [InlineData("Test-Pass-1!")]
    [InlineData("Admin-Test-1!")]
    [InlineData("Seed-Student-Test-1!")]
    [InlineData("Local-Test-Admin-1!")]
    [InlineData("Password1!")]
    [InlineData("NewPassword1!")]
    [InlineData("Wrong-Pass-9!")]
    [InlineData("Zebra-Quartz-42")]
    public void IsCommon_PasswordsTheTestsUse_AreNotListed(string password)
    {
        CommonPasswords.IsCommon(password).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void IsCommon_NothingEntered_IsNotCommon(string? password)
    {
        CommonPasswords.IsCommon(password).ShouldBeFalse();
    }

    [Fact]
    public void List_HoldsAboutTwoHundredLongEnoughLowercaseEntries()
    {
        CommonPasswords.All.Count.ShouldBeGreaterThanOrEqualTo(200);
        CommonPasswords.All.ShouldAllBe(entry => entry.Length >= PasswordRules.MinimumLength);
        foreach (var entry in CommonPasswords.All)
            entry.ShouldBe(entry.ToLowerInvariant());
    }

    [Fact]
    public async Task Validator_CommonPassword_FailsWithTheGreekMessageOnTheGreekSite()
    {
        using var cultureScope = new CultureScope("el-GR");

        var result = await _validator.ValidateAsync(IdentityMocks.MockUserManager(), Member(), "Password123");

        result.Succeeded.ShouldBeFalse();
        var error = result.Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe(CommonPasswordValidator.ErrorCode);
        error.Description.ShouldBe("Αυτός ο κωδικός είναι πολύ συνηθισμένος. Διάλεξε έναν πιο δύσκολο.");
    }

    [Fact]
    public async Task Validator_CommonPassword_FailsWithTheEnglishMessage()
    {
        using var cultureScope = new CultureScope("en-GB");

        var result = await _validator.ValidateAsync(IdentityMocks.MockUserManager(), Member(), "Password123");

        result.Errors.ShouldHaveSingleItem().Description.ShouldBe("That password is too common. Choose one that is harder to guess.");
    }

    [Fact]
    public async Task Validator_UncommonPassword_Succeeds()
    {
        var result = await _validator.ValidateAsync(IdentityMocks.MockUserManager(), Member(), "Zebra-Quartz-42");

        result.Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Abcdefg1", true)]
    [InlineData("Abcdef1", false)]
    [InlineData("abcdefg1", false)]
    [InlineData("Abcdefgh", false)]
    [InlineData("ABCDEFG1", false)]
    public void ComplexityPattern_FollowsTheSharedMinimumLength(string password, bool expected)
    {
        PasswordRules.MinimumLength.ShouldBe(8);
        Regex.IsMatch(password, PasswordRules.ComplexityPattern).ShouldBe(expected);
    }

    [Fact]
    public void ComplexityPatternWithMaximum_RejectsPasswordsLongerThanTheMaximum()
    {
        var atTheLimit = "A1" + new string('x', PasswordRules.MaximumLength - 2);

        Regex.IsMatch(atTheLimit, PasswordRules.ComplexityPatternWithMaximum).ShouldBeTrue();
        Regex.IsMatch(atTheLimit + "x", PasswordRules.ComplexityPatternWithMaximum).ShouldBeFalse();
    }
}

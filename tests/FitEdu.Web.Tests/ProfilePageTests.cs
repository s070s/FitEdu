using System.Globalization;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Enums;
using FitEdu.Domain.Identity;
using FitEdu.Shared.Resources;
using FitEdu.TestSupport;
using FitEdu.Web.Consumers;
using FitEdu.Web.Extensions;
using FitEdu.Web.Pages;
using FitEdu.Web.Services;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// bUnit tests for the profile page: the identity header, the three stat tiles (each hidden on its
/// own when its call fails) and the goal select that is saved with the training level.
/// </summary>
public class ProfilePageTests : BunitContext
{
    // The tests switch culture; Dispose puts the runner's cultures back.
    private readonly CultureScope _cultureScope = new();

    private readonly IProfileConsumer _profileConsumer = Substitute.For<IProfileConsumer>();
    private readonly ICourseConsumer _courseConsumer = Substitute.For<ICourseConsumer>();
    private readonly ICertificateConsumer _certificateConsumer = Substitute.For<ICertificateConsumer>();
    private readonly IAssessmentConsumer _assessmentConsumer = Substitute.For<IAssessmentConsumer>();

    private static readonly ProfileDto AliceProfile = new(
        "user-1", "alice@fitedu.local", "Alice", "Johnson", null, null, null, UserStatus.Intermediate, FitnessGoal.Strength);

    public ProfilePageTests()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en");
        Services.AddSingleton(_profileConsumer);
        Services.AddSingleton(_courseConsumer);
        Services.AddSingleton(_certificateConsumer);
        Services.AddSingleton(_assessmentConsumer);
        Services.AddSingleton(Substitute.For<IAuthService>());
        Services.AddSingleton(new AvatarChangedNotifier());
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        AddAuthorization().SetAuthorized("alice@fitedu.local");

        _profileConsumer.GetProfileAsync().Returns(AliceProfile);
        _courseConsumer.GetCoursesAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<string?>(),
            Arg.Any<FitnessGoal?>(), Arg.Any<CourseLevel?>(), Arg.Any<string?>()).Returns(new PagedResult<CourseListItemDto>(
            [Course(isEnrolled: true), Course(isEnrolled: true), Course(isEnrolled: false)], 3, 1, 100));
        _certificateConsumer.GetMyCertificatesAsync(Arg.Any<string>()).Returns(
            [new CertificateDto(Guid.NewGuid(), Guid.NewGuid(), "Alice Johnson", "Strength Foundations", DateTime.UtcNow, "Active")]);
        _assessmentConsumer.GetMySubmissionsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>()).Returns(
            new PagedResult<AssessmentSubmissionDto>([], 4, 1, 1));
    }

    private static CourseListItemDto Course(bool isEnrolled) =>
        new(Guid.NewGuid(), "Course", null, isEnrolled, TotalLessons: 6);

    [Fact]
    public void Loaded_RendersTheIdentityHeader()
    {
        var page = Render<Profile>();

        page.WaitForAssertion(() => page.Find(".profile-hero__name").TextContent.ShouldBe("Alice Johnson"));
        page.Find(".profile-hero__email").TextContent.ShouldBe("alice@fitedu.local");
        page.Find(".profile-hero .chip--lime").TextContent.ShouldContain(GlobalRes.StatusIntermediate);
        page.Find(".profile-hero .chip--teal").TextContent.ShouldContain(CourseRes.Goal_Strength);
    }

    [Fact]
    public void RejectedAvatar_DoesNotKeepShowingTheFileName()
    {
        var page = Render<Profile>();
        page.WaitForAssertion(() => page.Find("#profile-avatar"));

        page.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromText("not an image", "notes.txt", contentType: "text/plain"));

        page.Markup.ShouldContain(ProfileRes.InvalidImageType);
        page.Find(".file-picker__name").TextContent.ShouldBe(GlobalRes.NoFileChosen);
    }

    [Fact]
    public void PreferNotToSayLevel_ShowsNoLevelChip()
    {
        _profileConsumer.GetProfileAsync().Returns(AliceProfile with { Status = UserStatus.Other });

        var page = Render<Profile>();

        page.WaitForAssertion(() => page.Find(".profile-hero .chip--teal").TextContent.ShouldContain(CourseRes.Goal_Strength));
        page.FindAll(".profile-hero .chip--lime").ShouldBeEmpty();
    }

    [Fact]
    public void Loaded_RendersThreeStatTiles()
    {
        var page = Render<Profile>();

        page.WaitForAssertion(() => page.FindAll(".profile-stats .stat-tile").Count.ShouldBe(3));
        var values = page.FindAll(".profile-stats .stat-tile__value").Select(value => value.TextContent).ToList();
        values.ShouldBe(["2", "1", "4"]);
    }

    [Fact]
    public void StatCallFails_HidesThatTileOnly()
    {
        _certificateConsumer.GetMyCertificatesAsync(Arg.Any<string>()).ThrowsAsync(new HttpRequestException("down"));

        var page = Render<Profile>();

        page.WaitForAssertion(() => page.FindAll(".profile-stats .stat-tile").Count.ShouldBe(2));
        page.Find(".profile-stats").TextContent.ShouldNotContain(ProfileRes.StatsCertificates);
    }

    [Fact]
    public void GoalSelect_StartsAtTheSavedGoalAndOffersNotChosen()
    {
        var page = Render<Profile>();

        page.WaitForAssertion(() => page.Find("#profile-goal").GetAttribute("value").ShouldBe(nameof(FitnessGoal.Strength)));
        page.FindAll("#profile-goal option")[0].TextContent.ShouldBe(ProfileRes.GoalNotSet);
        page.FindAll("#profile-goal option").Count.ShouldBe(Enum.GetValues<FitnessGoal>().Length + 1);
    }

    [Fact]
    public void LevelSelect_WithoutASavedLevel_ShowsNotChosenInsteadOfABlankBox()
    {
        _profileConsumer.GetProfileAsync().Returns(AliceProfile with { Status = UserStatus.Unknown });

        var page = Render<Profile>();

        page.WaitForAssertion(() => page.Find("#profile-status").GetAttribute("value").ShouldBe(nameof(UserStatus.Unknown)));
        page.FindAll("#profile-status option")[0].TextContent.ShouldBe(ProfileRes.GoalNotSet);
    }

    [Fact]
    public void LevelSelect_WithASavedLevel_OffersOnlyRealLevels()
    {
        var page = Render<Profile>();

        page.WaitForAssertion(() => page.Find("#profile-status").GetAttribute("value").ShouldBe(nameof(UserStatus.Intermediate)));
        page.FindAll("#profile-status option").Count.ShouldBe(UserStatusLabels.Selectable.Length);
    }

    [Fact]
    public void SaveProfile_SendsTheChosenGoalAndLevel()
    {
        _profileConsumer.UpdateProfileAsync(Arg.Any<UpdateProfileRequest>()).Returns(AliceProfile);
        var page = Render<Profile>();
        page.WaitForAssertion(() => page.Find("#profile-goal"));

        page.Find("#profile-goal").Change(nameof(FitnessGoal.Endurance));
        page.Find("#profile-status").Change(nameof(UserStatus.Advanced));
        page.FindAll("button").First(button => button.TextContent.Contains(ProfileRes.BtnSaveProfile)).Click();

        _profileConsumer.Received(1).UpdateProfileAsync(Arg.Is<UpdateProfileRequest>(request =>
            request.Goal == FitnessGoal.Endurance && request.Status == UserStatus.Advanced));
    }

    [Fact]
    public void SaveProfile_NotChosenGoalSendsNull()
    {
        _profileConsumer.UpdateProfileAsync(Arg.Any<UpdateProfileRequest>()).Returns(AliceProfile);
        var page = Render<Profile>();
        page.WaitForAssertion(() => page.Find("#profile-goal"));

        page.Find("#profile-goal").Change(string.Empty);
        page.FindAll("button").First(button => button.TextContent.Contains(ProfileRes.BtnSaveProfile)).Click();

        _profileConsumer.Received(1).UpdateProfileAsync(Arg.Is<UpdateProfileRequest>(request => request.Goal == null));
    }

    [Fact]
    public void LanguageSelect_ShowsTheStoredLanguage()
    {
        _profileConsumer.GetProfileAsync().Returns(AliceProfile with { PreferredLanguage = UserLanguage.Greek });

        var page = Render<Profile>();

        page.WaitForAssertion(() => page.Find("#profile-language").GetAttribute("value").ShouldBe(UserLanguage.Greek));
        page.FindAll("#profile-language option").Select(option => option.GetAttribute("value"))
            .ShouldBe([UserLanguage.English, UserLanguage.Greek]);
    }

    [Fact]
    public void SaveProfile_NewLanguage_SavesItAndSwitchesTheSiteToIt()
    {
        _profileConsumer.UpdateProfileAsync(Arg.Any<UpdateProfileRequest>())
            .Returns(AliceProfile with { PreferredLanguage = UserLanguage.Greek });
        var navigation = Services.GetRequiredService<NavigationManager>();
        var page = Render<Profile>();
        page.WaitForAssertion(() => page.Find("#profile-language"));

        page.Find("#profile-language").Change(UserLanguage.Greek);
        page.FindAll("button").First(button => button.TextContent.Contains(ProfileRes.BtnSaveProfile)).Click();

        _profileConsumer.Received(1).UpdateProfileAsync(Arg.Is<UpdateProfileRequest>(request =>
            request.PreferredLanguage == UserLanguage.Greek));
        navigation.Uri.ShouldContain("culture/set?culture=el-GR");
    }

    [Fact]
    public void SaveProfile_SameLanguage_StaysOnThePage()
    {
        _profileConsumer.UpdateProfileAsync(Arg.Any<UpdateProfileRequest>()).Returns(AliceProfile);
        var navigation = Services.GetRequiredService<NavigationManager>();
        var page = Render<Profile>();
        page.WaitForAssertion(() => page.Find("#profile-language"));
        var uriBefore = navigation.Uri;

        page.FindAll("button").First(button => button.TextContent.Contains(ProfileRes.BtnSaveProfile)).Click();

        _profileConsumer.Received(1).UpdateProfileAsync(Arg.Is<UpdateProfileRequest>(request =>
            request.PreferredLanguage == UserLanguage.English));
        navigation.Uri.ShouldBe(uriBefore);
    }

    [Fact]
    public void SaveProfile_GreekSiteWithStoredEnglish_UntouchedLanguage_StaysOnThePage()
    {
        // The site reads in Greek (cookie or Accept-Language) while the account still stores English.
        CultureInfo.CurrentUICulture = new CultureInfo("el-GR");
        _profileConsumer.UpdateProfileAsync(Arg.Any<UpdateProfileRequest>())
            .Returns(AliceProfile with { DisplayName = "Ali" });
        var navigation = Services.GetRequiredService<NavigationManager>();
        var page = Render<Profile>();
        page.WaitForAssertion(() => page.Find("#profile-language"));
        var uriBefore = navigation.Uri;

        page.FindAll("button").First(button => button.TextContent.Contains(ProfileRes.BtnSaveProfile)).Click();

        _profileConsumer.Received(1).UpdateProfileAsync(Arg.Is<UpdateProfileRequest>(request =>
            request.PreferredLanguage == UserLanguage.English));
        navigation.Uri.ShouldBe(uriBefore);
    }

    [Fact]
    public void SaveProfile_Success_ShowsASuccessMessage()
    {
        _profileConsumer.UpdateProfileAsync(Arg.Any<UpdateProfileRequest>()).Returns(AliceProfile);
        var page = Render<Profile>();
        page.WaitForAssertion(() => page.Find("#profile-goal"));

        page.FindAll("button").First(button => button.TextContent.Contains(ProfileRes.BtnSaveProfile)).Click();

        var alert = page.Find(".alert[role=alert]");
        alert.TextContent.ShouldContain(ProfileRes.ProfileUpdatedSuccess);
        alert.ClassList.ShouldContain("alert-success");
    }

    [Fact]
    public void SaveProfile_Failure_ShowsAnErrorMessage()
    {
        _profileConsumer.UpdateProfileAsync(Arg.Any<UpdateProfileRequest>()).Returns((ProfileDto?)null);
        var page = Render<Profile>();
        page.WaitForAssertion(() => page.Find("#profile-goal"));

        page.FindAll("button").First(button => button.TextContent.Contains(ProfileRes.BtnSaveProfile)).Click();

        var alert = page.Find(".alert[role=alert]");
        alert.TextContent.ShouldContain(ProfileRes.ErrorUpdatingProfile);
        alert.ClassList.ShouldContain("alert-danger");
        alert.ClassList.ShouldNotContain("alert-info");
    }

    [Fact]
    public void SaveProfile_Exception_ShowsTheErrorText()
    {
        _profileConsumer.UpdateProfileAsync(Arg.Any<UpdateProfileRequest>()).ThrowsAsync(new HttpRequestException("down"));
        var page = Render<Profile>();
        page.WaitForAssertion(() => page.Find("#profile-goal"));

        page.FindAll("button").First(button => button.TextContent.Contains(ProfileRes.BtnSaveProfile)).Click();

        page.Find(".alert[role=alert]").TextContent.ShouldContain("Error: down");
    }

    protected override void Dispose(bool disposing)
    {
        _cultureScope.Dispose();
        base.Dispose(disposing);
    }
}

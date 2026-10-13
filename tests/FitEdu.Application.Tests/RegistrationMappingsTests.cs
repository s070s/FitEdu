using FitEdu.Application.DTOs;
using FitEdu.Application.Mappings;
using FitEdu.Domain.Enums;
using Shouldly;
using Xunit;

namespace FitEdu.Application.Tests;

/// <summary>
/// Both register paths (the Blazor page and POST api/auth/register) build the new user from the
/// same mapping, so the training level and goal rules live in one place.
/// </summary>
public class RegistrationMappingsTests
{
    private static RegisterRequestDto Request() => new()
    {
        Email = "new@fitedu.local",
        Password = "Password1",
        ConfirmPassword = "Password1",
        FirstName = "Maria",
        LastName = "Papadopoulou",
        GdprConsent = true
    };

    [Fact]
    public void ToNewUser_CopiesIdentityFieldsAndUsesEmailAsUserName()
    {
        var request = Request();
        request.DateOfBirth = new DateTime(1998, 4, 12, 15, 30, 0, DateTimeKind.Utc);

        var user = request.ToNewUser();

        user.UserName.ShouldBe("new@fitedu.local");
        user.Email.ShouldBe("new@fitedu.local");
        user.FirstName.ShouldBe("Maria");
        user.LastName.ShouldBe("Papadopoulou");
        user.DateOfBirth.ShouldBe(new DateOnly(1998, 4, 12));
        user.GdprConsentGiven.ShouldBeTrue();
        user.GdprConsentDate.ShouldNotBeNull();
    }

    [Fact]
    public void ToNewUser_UnknownTrainingLevel_BecomesBeginner()
    {
        var request = Request();
        request.Status = UserStatus.Unknown;

        request.ToNewUser().Status.ShouldBe(UserStatus.Beginner);
    }

    [Fact]
    public void ToNewUser_ChosenGoal_IsSaved()
    {
        var request = Request();
        request.Goal = FitnessGoal.Endurance;

        request.ToNewUser().Goal.ShouldBe(FitnessGoal.Endurance);
    }

    [Fact]
    public void ToNewUser_NoGoal_StaysEmpty()
    {
        Request().ToNewUser().Goal.ShouldBeNull();
    }

    [Fact]
    public void ToNewUser_GoalOutsideTheList_IsDropped()
    {
        var request = Request();
        request.Goal = (FitnessGoal)999;

        request.ToNewUser().Goal.ShouldBeNull();
    }
}

using System.ComponentModel.DataAnnotations;
using System.Globalization;
using FitEdu.Application.DTOs;
using FitEdu.Shared.Resources.Messages;
using Shouldly;
using Xunit;

namespace FitEdu.Application.Tests;

/// <summary>
/// Validates DataAnnotations on the class-based request DTOs via <see cref="Validator"/>.
///
/// NOTE: the record-style request DTOs (SaveCourseRequest, SaveBlogArticleRequest,
/// SaveTestimonialRequest, StartConversationRequest, SaveModuleRequest, SaveLessonRequest)
/// put their attributes on the positional *constructor parameters*, not the generated
/// properties. Raw <c>Validator.TryValidateObject</c> (property-based) does not see those;
/// ASP.NET MVC's ModelState does. Their validation is therefore asserted in the controller
/// integration tests (Phase 5), not here.
/// </summary>
public class RequestDtoValidationTests
{
    private static bool IsValid(object model)
    {
        var ctx = new ValidationContext(model);
        return Validator.TryValidateObject(model, ctx, new List<ValidationResult>(), validateAllProperties: true);
    }

    private static readonly CultureInfo Greek = new("el-GR");

    /// <summary>Validation messages as a Greek member sees them.</summary>
    private static List<string> GreekErrors(object model)
    {
        var previousCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = Greek;
        try
        {
            var results = new List<ValidationResult>();
            Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
            return results.Select(result => result.ErrorMessage ?? string.Empty).ToList();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    private static string GreekMessage(string resourceName) =>
        ErrorMessagesRes.ResourceManager.GetString(resourceName, Greek)!;

    private static RegisterRequestDto ValidRegister() => new()
    {
        Email = "user@example.com",
        Password = "Password1",
        ConfirmPassword = "Password1",
        FirstName = "First",
        LastName = "Last",
        GdprConsent = true
    };

    [Fact]
    public void Register_Valid_Passes() => IsValid(ValidRegister()).ShouldBeTrue();

    [Fact]
    public void Register_InvalidEmail_Fails()
    {
        var dto = ValidRegister();
        dto.Email = "not-an-email";
        IsValid(dto).ShouldBeFalse();
    }

    [Fact]
    public void Register_ShortPassword_Fails()
    {
        var dto = ValidRegister();
        dto.Password = dto.ConfirmPassword = "Ab1";
        IsValid(dto).ShouldBeFalse();
    }

    [Fact]
    public void Register_PasswordWithoutUppercase_Fails()
    {
        // UX-11: MinLength(8) alone let "password1" through even though the real Identity
        // policy (AuthenticationSetupExtensions) requires an uppercase letter too.
        var dto = ValidRegister();
        dto.Password = dto.ConfirmPassword = "password1";
        IsValid(dto).ShouldBeFalse();
    }

    [Fact]
    public void Register_PasswordWithoutDigit_Fails()
    {
        var dto = ValidRegister();
        dto.Password = dto.ConfirmPassword = "PasswordOnly";
        IsValid(dto).ShouldBeFalse();
    }

    [Fact]
    public void Register_ConfirmPasswordMismatch_Fails()
    {
        var dto = ValidRegister();
        dto.ConfirmPassword = "Different1";
        IsValid(dto).ShouldBeFalse();
    }

    [Fact]
    public void Register_GdprConsentFalse_Fails()
    {
        var dto = ValidRegister();
        dto.GdprConsent = false;
        IsValid(dto).ShouldBeFalse();
    }

    [Fact]
    public void Register_FirstNameTooLong_Fails()
    {
        var dto = ValidRegister();
        dto.FirstName = new string('x', 101);
        IsValid(dto).ShouldBeFalse();
    }

    [Fact]
    public void Login_Valid_Passes() =>
        IsValid(new LoginRequestDto { Email = "u@x.com", Password = "secret" }).ShouldBeTrue();

    [Fact]
    public void Login_MissingEmail_Fails() =>
        IsValid(new LoginRequestDto { Email = "", Password = "secret" }).ShouldBeFalse();

    [Fact]
    public void Login_InvalidEmail_Fails() =>
        IsValid(new LoginRequestDto { Email = "nope", Password = "secret" }).ShouldBeFalse();

    [Fact]
    public void Login_Empty_ErrorsFollowMemberLanguage()
    {
        // The Greek login page showed the framework default "The Password field is required.".
        var errors = GreekErrors(new LoginRequestDto());

        errors.ShouldBe(
            [GreekMessage(nameof(ErrorMessagesRes.Auth_EmailRequired)), GreekMessage(nameof(ErrorMessagesRes.Auth_PasswordRequired))],
            ignoreOrder: true);
    }

    [Fact]
    public void Login_InvalidEmail_ErrorFollowsMemberLanguage() =>
        GreekErrors(new LoginRequestDto { Email = "nope", Password = "secret" })
            .ShouldBe([GreekMessage(nameof(ErrorMessagesRes.Auth_EmailInvalid))]);

    [Fact]
    public void ForgotPassword_Empty_ErrorFollowsMemberLanguage() =>
        GreekErrors(new ForgotPasswordRequestDto())
            .ShouldBe([GreekMessage(nameof(ErrorMessagesRes.Auth_EmailRequired))]);

    [Fact]
    public void ForgotPassword_InvalidEmail_ErrorFollowsMemberLanguage() =>
        GreekErrors(new ForgotPasswordRequestDto { Email = "nope" })
            .ShouldBe([GreekMessage(nameof(ErrorMessagesRes.Auth_EmailInvalid))]);

    [Fact]
    public void ResetPassword_Valid_Passes() =>
        IsValid(new ResetPasswordRequestDto
        {
            Email = "u@x.com",
            Token = "tok",
            NewPassword = "Password1",
            ConfirmPassword = "Password1"
        }).ShouldBeTrue();

    [Fact]
    public void ResetPassword_Mismatch_Fails() =>
        IsValid(new ResetPasswordRequestDto
        {
            Email = "u@x.com",
            Token = "tok",
            NewPassword = "Password1",
            ConfirmPassword = "Nope12345"
        }).ShouldBeFalse();

    [Fact]
    public void ResetPassword_ShortPassword_Fails() =>
        IsValid(new ResetPasswordRequestDto
        {
            Email = "u@x.com",
            Token = "tok",
            NewPassword = "Ab1",
            ConfirmPassword = "Ab1"
        }).ShouldBeFalse();

    [Fact]
    public void ResetPassword_WithoutUppercase_Fails() =>
        IsValid(new ResetPasswordRequestDto
        {
            Email = "u@x.com",
            Token = "tok",
            NewPassword = "password1",
            ConfirmPassword = "password1"
        }).ShouldBeFalse();
}

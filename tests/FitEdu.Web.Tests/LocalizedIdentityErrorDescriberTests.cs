using System.Globalization;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using FitEdu.Application.DTOs;
using FitEdu.Web.Services;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// Identity's password-rule and email errors follow the member's UI language and keep Identity's codes.
/// </summary>
public class LocalizedIdentityErrorDescriberTests : IClassFixture<SelfCallingWebAppFactory>
{
    private readonly SelfCallingWebAppFactory _factory;
    private readonly LocalizedIdentityErrorDescriber _describer = new();

    public LocalizedIdentityErrorDescriberTests(SelfCallingWebAppFactory factory) => _factory = factory;

    [Fact]
    public void English_PasswordRulesAndEmailErrors()
    {
        CultureInfo.CurrentCulture = new CultureInfo("en-GB");
        CultureInfo.CurrentUICulture = new CultureInfo("en-GB");

        _describer.PasswordTooShort(8).Description.ShouldBe("Your password needs at least 8 characters.");
        _describer.PasswordRequiresDigit().Description.ShouldBe("Your password needs at least one digit (0-9).");
        _describer.PasswordRequiresLower().Description.ShouldBe("Your password needs at least one lowercase letter (a-z).");
        _describer.PasswordRequiresUpper().Description.ShouldBe("Your password needs at least one uppercase letter (A-Z).");
        _describer.PasswordRequiresNonAlphanumeric().Description.ShouldBe("Your password needs at least one symbol, such as ! or #.");
        _describer.PasswordRequiresUniqueChars(3).Description.ShouldBe("Your password needs at least 3 different characters.");
        _describer.DuplicateEmail("maria@fitedu.local").Description.ShouldBe("The email maria@fitedu.local is already registered.");
        _describer.InvalidEmail("maria").Description.ShouldBe("The email maria is not valid.");
    }

    [Fact]
    public void Greek_PasswordRulesAndEmailErrors()
    {
        CultureInfo.CurrentCulture = new CultureInfo("el-GR");
        CultureInfo.CurrentUICulture = new CultureInfo("el-GR");

        _describer.PasswordTooShort(8).Description.ShouldBe("Ο κωδικός σου χρειάζεται τουλάχιστον 8 χαρακτήρες.");
        _describer.PasswordRequiresDigit().Description.ShouldBe("Ο κωδικός σου χρειάζεται τουλάχιστον ένα ψηφίο (0-9).");
        _describer.PasswordRequiresLower().Description.ShouldBe("Ο κωδικός σου χρειάζεται τουλάχιστον ένα πεζό γράμμα (a-z).");
        _describer.PasswordRequiresUpper().Description.ShouldBe("Ο κωδικός σου χρειάζεται τουλάχιστον ένα κεφαλαίο γράμμα (A-Z).");
        _describer.PasswordRequiresNonAlphanumeric().Description.ShouldBe("Ο κωδικός σου χρειάζεται τουλάχιστον ένα σύμβολο, όπως ! ή #.");
        _describer.PasswordRequiresUniqueChars(3).Description.ShouldBe("Ο κωδικός σου χρειάζεται τουλάχιστον 3 διαφορετικούς χαρακτήρες.");
        _describer.DuplicateEmail("maria@fitedu.local").Description.ShouldBe("Το email maria@fitedu.local είναι ήδη εγγεγραμμένο.");
        _describer.InvalidEmail("maria").Description.ShouldBe("Το email maria δεν είναι έγκυρο.");
    }

    [Fact]
    public void Errors_KeepIdentitysCodes()
    {
        _describer.PasswordTooShort(8).Code.ShouldBe("PasswordTooShort");
        _describer.DuplicateEmail("maria@fitedu.local").Code.ShouldBe("DuplicateEmail");
        _describer.InvalidEmail("maria").Code.ShouldBe("InvalidEmail");
    }

    [Fact]
    public async Task ApiRegister_FromAGreekPage_AnswersWithGreekPasswordErrors()
    {
        // The page's loopback client sends the Greek UI language, so the API's Identity errors are Greek.
        CultureInfo.CurrentUICulture = new CultureInfo("el-GR");
        var client = _factory.Services.GetRequiredService<IHttpClientFactory>().CreateClient("SelfClient");
        var request = new RegisterRequestDto
        {
            Email = $"greek-{Guid.NewGuid():N}@test.com",
            // Passes the form's rule (8+ characters, a capital and a digit) but not Identity's lowercase rule.
            Password = "PASSWORD1",
            ConfirmPassword = "PASSWORD1",
            FirstName = "Maria",
            LastName = "Papa",
            DateOfBirth = new DateTime(2000, 1, 1),
            GdprConsent = true
        };

        var response = await client.PostAsJsonAsync("api/auth/register", request);
        var result = await response.Content.ReadFromJsonAsync<AuthResponseDto>();

        result.ShouldNotBeNull().Errors.ShouldNotBeNull()
            .ShouldContain("Ο κωδικός σου χρειάζεται τουλάχιστον ένα πεζό γράμμα (a-z).");
    }
}

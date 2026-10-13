using FitEdu.Infrastructure.ApiServices;
using FitEdu.Infrastructure.Configuration;
using FitEdu.TestSupport;
using Shouldly;
using Xunit;

namespace FitEdu.Infrastructure.Tests;

public sealed class SmtpEmailServiceTests : IDisposable
{
    // A link with a query string: the HTML body must encode the "&", the text body keeps it raw.
    private const string ConfirmationLink = "https://fitedu.test/confirm-email?userId=u1&token=abc";
    private const string ResetLink = "https://fitedu.test/reset-password?email=u%40x.com&token=xyz";

    private static EmailOptions Options() => new()
    {
        Smtp = new SmtpOptions { FromAddress = "no-reply@fitedu.test", FromName = "FitEdu" }
    };

    // The email language follows the UI culture of the sending request; English unless a test says otherwise.
    // Dispose puts the runner's cultures back.
    private readonly CultureScope _cultureScope = new("en-GB");

    [Fact]
    public void BuildMessage_EmailConfirmation_IsTheFitnessWelcomeWithTheLink()
    {
        var message = SmtpEmailService.BuildMessage(
            Options(), "user@example.com", EmailTemplates.EmailConfirmation(ConfirmationLink));

        message.From.Mailboxes.ShouldHaveSingleItem().Address.ShouldBe("no-reply@fitedu.test");
        message.To.Mailboxes.ShouldHaveSingleItem().Address.ShouldBe("user@example.com");
        message.Subject.ShouldBe("Welcome to FitEdu — let's get moving");
        var html = message.HtmlBody.ShouldNotBeNull();
        html.ShouldContain("exercise library");
        html.ShouldContain("https://fitedu.test/confirm-email?userId=u1&amp;token=abc", Case.Sensitive);
        message.TextBody.ShouldNotBeNull().ShouldContain(ConfirmationLink, Case.Sensitive);
    }

    [Fact]
    public void BuildMessage_PasswordReset_KeepsTheMemberTrainingWithTheLink()
    {
        var message = SmtpEmailService.BuildMessage(
            Options(), "user@example.com", EmailTemplates.PasswordReset(ResetLink));

        message.Subject.ShouldBe("Reset your FitEdu password");
        var html = message.HtmlBody.ShouldNotBeNull();
        html.ShouldContain("https://fitedu.test/reset-password?email=u%40x.com&amp;token=xyz", Case.Sensitive);
        html.ShouldContain("If you did not ask for this");
        message.TextBody.ShouldNotBeNull().ShouldContain(ResetLink, Case.Sensitive);
    }

    [Fact]
    public void BuildMessage_EmailConfirmation_InGreek_IsTheGreekWelcomeWithTheLink()
    {
        using var cultureScope = new CultureScope("el-GR");

        var message = SmtpEmailService.BuildMessage(
            Options(), "user@example.com", EmailTemplates.EmailConfirmation(ConfirmationLink));

        message.Subject.ShouldBe("Καλώς ήρθες στο FitEdu — ώρα για κίνηση");
        var html = message.HtmlBody.ShouldNotBeNull();
        html.ShouldContain("βιβλιοθήκη ασκήσεων");
        html.ShouldContain("Επιβεβαίωση email");
        html.ShouldContain("https://fitedu.test/confirm-email?userId=u1&amp;token=abc", Case.Sensitive);
        var text = message.TextBody.ShouldNotBeNull();
        text.ShouldContain(ConfirmationLink, Case.Sensitive);
        text.ShouldContain("Καλή προπόνηση");
    }

    [Fact]
    public void BuildMessage_PasswordReset_InGreek_IsTheGreekResetWithTheLink()
    {
        using var cultureScope = new CultureScope("el-GR");

        var message = SmtpEmailService.BuildMessage(
            Options(), "user@example.com", EmailTemplates.PasswordReset(ResetLink));

        message.Subject.ShouldBe("Επαναφορά του κωδικού σου στο FitEdu");
        var html = message.HtmlBody.ShouldNotBeNull();
        html.ShouldContain("https://fitedu.test/reset-password?email=u%40x.com&amp;token=xyz", Case.Sensitive);
        html.ShouldContain("Αν δεν το ζήτησες εσύ");
        message.TextBody.ShouldNotBeNull().ShouldContain(ResetLink, Case.Sensitive);
    }

    [Fact]
    public void EmailTemplates_HtmlEncodeALinkThatCarriesMarkup()
    {
        var content = EmailTemplates.EmailConfirmation("https://fitedu.test/?x=\"><script>");

        content.HtmlBody.ShouldNotContain("<script>");
        content.HtmlBody.ShouldContain("&quot;&gt;&lt;script&gt;", Case.Sensitive);
    }

    public void Dispose() => _cultureScope.Dispose();
}

using FitEdu.Infrastructure.ApiServices;
using FitEdu.Infrastructure.Configuration;
using Shouldly;
using Xunit;

namespace FitEdu.Infrastructure.Tests;

public class SmtpEmailServiceTests
{
    private static EmailOptions Options() => new()
    {
        Smtp = new SmtpOptions { FromAddress = "no-reply@fitedu.test", FromName = "FitEdu" }
    };

    [Fact]
    public void BuildMessage_SetsFromToSubjectAndBodies()
    {
        var msg = SmtpEmailService.BuildMessage(
            Options(), "user@example.com", "Confirm your email",
            htmlBody: "<a href=\"https://fitedu.test/confirm-email?token=abc\">Confirm</a>",
            textBody: "Confirm: https://fitedu.test/confirm-email?token=abc");

        msg.From.Mailboxes.ShouldHaveSingleItem().Address.ShouldBe("no-reply@fitedu.test");
        msg.To.Mailboxes.ShouldHaveSingleItem().Address.ShouldBe("user@example.com");
        msg.Subject.ShouldBe("Confirm your email");
        msg.HtmlBody.ShouldContain("/confirm-email?token=abc");
        msg.TextBody.ShouldContain("/confirm-email?token=abc");
    }
}

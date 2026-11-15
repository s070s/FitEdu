using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using NSubstitute;
using FitEdu.Domain.Identity;
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

    private const string English = UserLanguage.English;
    private const string Greek = UserLanguage.Greek;

    // The email language is the one the caller passes, never the UI culture: the sender runs in English
    // here, so a Greek email proves the culture is ignored. Dispose puts the runner's cultures back.
    private readonly CultureScope _cultureScope = new("en-GB");

    [Fact]
    public void BuildMessage_EmailConfirmation_IsTheFitnessWelcomeWithTheLink()
    {
        var message = SmtpEmailService.BuildMessage(
            Options(), "user@example.com", EmailTemplates.EmailConfirmation(ConfirmationLink, English));

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
            Options(), "user@example.com", EmailTemplates.PasswordReset(ResetLink, English));

        message.Subject.ShouldBe("Reset your FitEdu password");
        var html = message.HtmlBody.ShouldNotBeNull();
        html.ShouldContain("https://fitedu.test/reset-password?email=u%40x.com&amp;token=xyz", Case.Sensitive);
        html.ShouldContain("you can ignore this email");
        message.TextBody.ShouldNotBeNull().ShouldContain(ResetLink, Case.Sensitive);
    }

    [Fact]
    public void BuildMessage_EmailConfirmation_InGreek_IsTheGreekWelcomeWithTheLink()
    {
        var message = SmtpEmailService.BuildMessage(
            Options(), "user@example.com", EmailTemplates.EmailConfirmation(ConfirmationLink, Greek));

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
        var message = SmtpEmailService.BuildMessage(
            Options(), "user@example.com", EmailTemplates.PasswordReset(ResetLink, Greek));

        message.Subject.ShouldBe("Επαναφορά του κωδικού πρόσβασής σου στο FitEdu");
        var html = message.HtmlBody.ShouldNotBeNull();
        html.ShouldContain("https://fitedu.test/reset-password?email=u%40x.com&amp;token=xyz", Case.Sensitive);
        html.ShouldContain("αγνόησε αυτό το email");
        message.TextBody.ShouldNotBeNull().ShouldContain(ResetLink, Case.Sensitive);
    }

    // Both parts of the message, HTML and plain text, carry the lifetime and the ignore line.
    private static void ShouldStateLifetimeAnd(MimeMessage message, string lifetimeLine, string ignoreLine)
    {
        foreach (var body in new[] { message.HtmlBody.ShouldNotBeNull(), message.TextBody.ShouldNotBeNull() })
        {
            body.ShouldContain(lifetimeLine, Case.Sensitive);
            body.ShouldContain(ignoreLine, Case.Sensitive);
        }
    }

    [Fact]
    public void BuildMessage_Confirmation_StatesLifetimeAndIgnoreLine_EnEl()
    {
        var english = SmtpEmailService.BuildMessage(
            Options(), "user@example.com", EmailTemplates.EmailConfirmation(ConfirmationLink, English));
        var greek = SmtpEmailService.BuildMessage(
            Options(), "user@example.com", EmailTemplates.EmailConfirmation(ConfirmationLink, Greek));

        ShouldStateLifetimeAnd(english, "This link works for 24 hours.", "If you didn't create a FitEdu account, you can ignore this email.");
        ShouldStateLifetimeAnd(greek, "Ο σύνδεσμος ισχύει για 24 ώρες.", "Αν δεν δημιούργησες εσύ λογαριασμό στο FitEdu, αγνόησε αυτό το email.");
    }

    [Fact]
    public void BuildMessage_PasswordReset_StatesLifetimeAndIgnoreLine_EnEl()
    {
        var english = SmtpEmailService.BuildMessage(
            Options(), "user@example.com", EmailTemplates.PasswordReset(ResetLink, English));
        var greek = SmtpEmailService.BuildMessage(
            Options(), "user@example.com", EmailTemplates.PasswordReset(ResetLink, Greek));

        ShouldStateLifetimeAnd(english, "This link works for 24 hours.", "If you didn't ask to reset your password, you can ignore this email.");
        ShouldStateLifetimeAnd(greek, "Ο σύνδεσμος ισχύει για 24 ώρες.", "Αν δεν ζήτησες εσύ αλλαγή κωδικού, αγνόησε αυτό το email.");
    }

    [Theory]
    [InlineData(UserLanguage.English, "en")]
    [InlineData(UserLanguage.Greek, "el")]
    public void EmailTemplates_HtmlIsAFullDocumentInTheEmailLanguage(string language, string htmlLang)
    {
        EmailTemplates.EmailConfirmation(ConfirmationLink, language).HtmlBody.ShouldStartWith($"<html lang=\"{htmlLang}\">", Case.Sensitive);
        EmailTemplates.PasswordReset(ResetLink, language).HtmlBody.ShouldStartWith($"<html lang=\"{htmlLang}\">", Case.Sensitive);
        EmailTemplates.PasswordReset(ResetLink, language).HtmlBody.ShouldEndWith("</html>", Case.Sensitive);
    }

    [Fact]
    public void EmailTemplates_TheStatedLifetimeIsTheTokenLifetimeConstant()
    {
        EmailTemplates.LinkLifetimeHours.ShouldBe(24); // Identity's default: the stated lifetime changes no behaviour
        EmailTemplates.EmailConfirmation(ConfirmationLink, English).TextBody
            .ShouldContain($"works for {EmailTemplates.LinkLifetimeHours} hours", Case.Sensitive);
    }

    [Fact]
    public void EmailTemplates_HtmlEncodeALinkThatCarriesMarkup()
    {
        var content = EmailTemplates.EmailConfirmation("https://fitedu.test/?x=\"><script>", English);

        content.HtmlBody.ShouldNotContain("<script>");
        content.HtmlBody.ShouldContain("&quot;&gt;&lt;script&gt;", Case.Sensitive);
    }

    [Fact]
    public void EmailTemplates_GreekLanguageWinsOverAnEnglishSender()
    {
        // The admin who forces a reset reads the site in English; the Greek member still gets Greek.
        EmailTemplates.PasswordReset(ResetLink, Greek).Subject.ShouldBe("Επαναφορά του κωδικού πρόσβασής σου στο FitEdu");
    }

    [Fact]
    public void EmailTemplates_UnknownLanguage_FallsBackToEnglish()
    {
        using var cultureScope = new CultureScope("el-GR");

        EmailTemplates.PasswordReset(ResetLink, "fr").Subject.ShouldBe("Reset your FitEdu password");
    }

    // ---- SendAsync, through a fake SMTP client ----------------------------------

    private const string PrivateAddress = "private.person@secret-mail.test";

    private readonly RecordingLogger<SmtpEmailService> _logger = new();

    private SmtpEmailService NewService(ISmtpClient client, EmailOptions? options = null) =>
        new(Microsoft.Extensions.Options.Options.Create(options ?? Options()), _logger, () => client);

    [Fact]
    public async Task SendAsync_LogsWithoutTheRecipientAddress()
    {
        var client = Substitute.For<ISmtpClient>();

        await NewService(client).SendEmailConfirmationAsync(PrivateAddress, ConfirmationLink, English);

        var entry = _logger.Information.ShouldHaveSingleItem();
        entry.ShouldContain("Welcome to FitEdu", Case.Sensitive);
        entry.ShouldNotContain(PrivateAddress);
        entry.ShouldNotContain("secret-mail");
    }

    [Fact]
    public async Task SendAsync_SendsTheMessageToTheRecipient()
    {
        var client = Substitute.For<ISmtpClient>();

        await NewService(client).SendPasswordResetAsync(PrivateAddress, ResetLink, English);

        await client.Received(1).SendAsync(
            Arg.Is<MimeMessage>(message => message.To.Mailboxes.Single().Address == PrivateAddress),
            Arg.Any<CancellationToken>(), Arg.Any<ITransferProgress>());
    }

    [Fact]
    public async Task SendAsync_UsesTheConfiguredTimeout()
    {
        var client = Substitute.For<ISmtpClient>();

        await NewService(client).SendEmailConfirmationAsync(PrivateAddress, ConfirmationLink, English);

        client.Timeout.ShouldBe(15_000); // the default: 15 seconds, not MailKit's two minutes
    }

    [Fact]
    public async Task SendAsync_TimeoutSecondsOption_SetsTheClientTimeout()
    {
        var client = Substitute.For<ISmtpClient>();
        var options = Options();
        options.Smtp.TimeoutSeconds = 40;

        await NewService(client, options).SendEmailConfirmationAsync(PrivateAddress, ConfirmationLink, English);

        client.Timeout.ShouldBe(40_000);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(301)]
    public void Validator_RefusesATimeoutOutsideOneToThreeHundredSeconds(int seconds)
    {
        var options = Options();
        options.Smtp.TimeoutSeconds = seconds;

        var result = new EmailOptionsValidator(isDevelopment: false).Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("TimeoutSeconds", Case.Sensitive);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void Validator_RefusesAPortOutsideTheValidRange(int port)
    {
        var options = Options();
        options.Smtp.Port = port;

        var result = new EmailOptionsValidator(isDevelopment: false).Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Email:Smtp:Port", Case.Sensitive);
    }

    [Theory]
    [InlineData(587, true, SecureSocketOptions.StartTls)]
    [InlineData(465, true, SecureSocketOptions.SslOnConnect)]
    [InlineData(465, false, SecureSocketOptions.SslOnConnect)] // port 465 is TLS from the first byte, whatever UseStartTls says
    [InlineData(25, false, SecureSocketOptions.None)]
    public async Task SendAsync_PicksTheSocketSecurityFromThePortAndUseStartTls(int port, bool useStartTls, SecureSocketOptions expected)
    {
        var client = Substitute.For<ISmtpClient>();
        var options = Options();
        options.Smtp.Host = "relay.fitedu.test";
        options.Smtp.Port = port;
        options.Smtp.UseStartTls = useStartTls;

        await NewService(client, options).SendEmailConfirmationAsync(PrivateAddress, ConfirmationLink, English);

        await client.Received(1).ConnectAsync("relay.fitedu.test", port, expected, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(25, false, false)]
    [InlineData(587, true, true)]
    [InlineData(465, false, true)]
    public void Validator_CredentialsNeedTls_OutsideDevelopment(int port, bool useStartTls, bool accepted)
    {
        var options = Options();
        options.Smtp.Port = port;
        options.Smtp.UseStartTls = useStartTls;
        options.Smtp.Username = "relay-user";
        options.Smtp.Password = "not-a-real-password";

        var result = new EmailOptionsValidator(isDevelopment: false).Validate(null, options);

        result.Succeeded.ShouldBe(accepted);
        if (!accepted)
            result.FailureMessage.ShouldNotBeNull().ShouldContain("TLS", Case.Sensitive);
    }

    [Fact]
    public void Validator_CredentialsWithoutTls_AreAllowedInDevelopment()
    {
        var options = Options();
        options.Smtp.Port = 25;
        options.Smtp.UseStartTls = false;
        options.Smtp.Username = "relay-user";

        new EmailOptionsValidator(isDevelopment: true).Validate(null, options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validator_NoCredentialsWithoutTls_IsAllowedOutsideDevelopment()
    {
        var options = Options();
        options.Smtp.Port = 25;
        options.Smtp.UseStartTls = false;

        new EmailOptionsValidator(isDevelopment: false).Validate(null, options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validator_AcceptsTheDefaults()
    {
        new EmailOptionsValidator(isDevelopment: false).Validate(null, Options()).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task SendAsync_WhenTheSendFails_ThrowsTheSendError_NotTheCloseError()
    {
        var client = Substitute.For<ISmtpClient>();
        client.IsConnected.Returns(true);
        client.SendAsync(Arg.Any<MimeMessage>(), Arg.Any<CancellationToken>(), Arg.Any<ITransferProgress>())
            .Returns(Task.FromException<string>(new InvalidOperationException("the relay refused the message")));
        client.DisconnectAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new IOException("connection already closed")));

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            NewService(client).SendEmailConfirmationAsync(PrivateAddress, ConfirmationLink, English));

        thrown.Message.ShouldBe("the relay refused the message");
    }

    [Fact]
    public async Task SendAsync_WhenTheSendWorks_ClosesTheConnectionOnce_AndAFailedCloseDoesNotFailTheSend()
    {
        var client = Substitute.For<ISmtpClient>();
        client.IsConnected.Returns(true);
        client.DisconnectAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new IOException("connection already closed")));

        await NewService(client).SendEmailConfirmationAsync(PrivateAddress, ConfirmationLink, English);

        await client.Received(1).DisconnectAsync(true, Arg.Any<CancellationToken>());
        _logger.Information.ShouldHaveSingleItem().ShouldContain("sent", Case.Sensitive);
    }

    public void Dispose() => _cultureScope.Dispose();
}

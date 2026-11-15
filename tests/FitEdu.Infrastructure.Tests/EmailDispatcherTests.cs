using MailKit;
using MailKit.Net.Smtp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MimeKit;
using NSubstitute;
using FitEdu.Domain.Identity;
using FitEdu.Infrastructure.ApiServices;
using FitEdu.Infrastructure.Configuration;
using FitEdu.TestSupport;
using Shouldly;
using Xunit;

namespace FitEdu.Infrastructure.Tests;

public sealed class EmailDispatcherTests
{
    private const string PrivateAddress = "private.person@secret-mail.test";
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly RecordingLogger<EmailDispatcher> _dispatcherLog = new();
    private readonly EmailOutbox _queue = new();

    // The dispatcher sends through SmtpEmailService resolved from its own scope; the fake client stands in for the relay.
    private EmailDispatcher NewDispatcher(ISmtpClient client)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => new SmtpEmailService(
            Options.Create(new EmailOptions()), new RecordingLogger<SmtpEmailService>(), () => client));
        return new EmailDispatcher(_queue, services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), _dispatcherLog);
    }

    private static QueuedEmail Email(string to, string link) =>
        new(to, EmailTemplates.PasswordReset(link, UserLanguage.English));

    [Fact]
    public async Task QueuedEmail_IsSentThroughTheTransport()
    {
        var sent = new TaskCompletionSource<MimeMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = Substitute.For<ISmtpClient>();
        client.SendAsync(Arg.Do<MimeMessage>(message => sent.TrySetResult(message)), Arg.Any<CancellationToken>(), Arg.Any<ITransferProgress>())
            .Returns(Task.FromResult("ok"));
        var dispatcher = NewDispatcher(client);
        await dispatcher.StartAsync(CancellationToken.None);

        _queue.TryEnqueue(Email(PrivateAddress, "https://fitedu.test/reset")).ShouldBeTrue();
        var message = await sent.Task.WaitAsync(Patience);
        await dispatcher.StopAsync(CancellationToken.None);

        message.To.Mailboxes.Single().Address.ShouldBe(PrivateAddress);
    }

    [Fact]
    public async Task AFailedSend_IsLoggedBySubjectOnly_AndTheNextEmailStillGoes()
    {
        var second = new TaskCompletionSource<MimeMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = Substitute.For<ISmtpClient>();
        client.SendAsync(Arg.Any<MimeMessage>(), Arg.Any<CancellationToken>(), Arg.Any<ITransferProgress>())
            .Returns(
                _ => Task.FromException<string>(new SmtpCommandException(
                    SmtpErrorCode.RecipientNotAccepted, SmtpStatusCode.MailboxUnavailable,
                    new MailboxAddress(string.Empty, PrivateAddress), $"<{PrivateAddress}> recipient rejected")),
                call =>
                {
                    second.TrySetResult(call.Arg<MimeMessage>());
                    return Task.FromResult("ok");
                });
        var dispatcher = NewDispatcher(client);
        await dispatcher.StartAsync(CancellationToken.None);

        _queue.TryEnqueue(Email(PrivateAddress, "https://fitedu.test/one")).ShouldBeTrue();
        _queue.TryEnqueue(Email("other@fitedu.test", "https://fitedu.test/two")).ShouldBeTrue();
        await second.Task.WaitAsync(Patience);
        await dispatcher.StopAsync(CancellationToken.None);

        var entry = _dispatcherLog.Errors.ShouldHaveSingleItem();
        entry.ShouldContain(EmailTemplates.PasswordResetSubject, Case.Sensitive);
        // The relay's status code (550) and MailKit's error code say why it failed, without the address.
        entry.ShouldContain("550");
        entry.ShouldContain(nameof(SmtpErrorCode.RecipientNotAccepted));
        entry.ShouldNotContain("secret-mail");
    }

    [Fact]
    public async Task AFailedSendOfAnotherKind_IsLoggedBySubjectAndExceptionTypeOnly()
    {
        var second = new TaskCompletionSource<MimeMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = Substitute.For<ISmtpClient>();
        client.SendAsync(Arg.Any<MimeMessage>(), Arg.Any<CancellationToken>(), Arg.Any<ITransferProgress>())
            .Returns(
                _ => Task.FromException<string>(new InvalidOperationException($"550 <{PrivateAddress}> recipient rejected")),
                call =>
                {
                    second.TrySetResult(call.Arg<MimeMessage>());
                    return Task.FromResult("ok");
                });
        var dispatcher = NewDispatcher(client);
        await dispatcher.StartAsync(CancellationToken.None);

        _queue.TryEnqueue(Email(PrivateAddress, "https://fitedu.test/one")).ShouldBeTrue();
        _queue.TryEnqueue(Email("other@fitedu.test", "https://fitedu.test/two")).ShouldBeTrue();
        await second.Task.WaitAsync(Patience);
        await dispatcher.StopAsync(CancellationToken.None);

        // The exception text may carry the address, so only its type is logged.
        var entry = _dispatcherLog.Errors.ShouldHaveSingleItem();
        entry.ShouldContain(EmailTemplates.PasswordResetSubject, Case.Sensitive);
        entry.ShouldContain(nameof(InvalidOperationException));
        entry.ShouldNotContain("secret-mail");
    }

    [Fact]
    public async Task Stop_WithAnEmptyQueue_FinishesWithoutError()
    {
        var dispatcher = NewDispatcher(Substitute.For<ISmtpClient>());
        await dispatcher.StartAsync(CancellationToken.None);

        await dispatcher.StopAsync(CancellationToken.None).WaitAsync(Patience);

        _dispatcherLog.Errors.ShouldBeEmpty();
    }
}

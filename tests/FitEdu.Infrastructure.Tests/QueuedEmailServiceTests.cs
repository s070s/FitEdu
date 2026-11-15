using FitEdu.Domain.Identity;
using FitEdu.Infrastructure.ApiServices;
using Shouldly;
using Xunit;

namespace FitEdu.Infrastructure.Tests;

public sealed class QueuedEmailServiceTests
{
    private const string Address = "member@fitedu.test";

    [Fact]
    public async Task Send_ReturnsBeforeTheTransportFinishes()
    {
        var queue = new EmailOutbox();
        var service = new QueuedEmailService(queue);

        var sending = service.SendEmailConfirmationAsync(Address, "https://fitedu.test/confirm-email", UserLanguage.English);

        // Nothing has read the queue, so no transport has run: the caller is already released.
        sending.IsCompletedSuccessfully.ShouldBeTrue();
        await sending;
        queue.Reader.TryRead(out var queued).ShouldBeTrue();
        queued!.To.ShouldBe(Address);
        queued.Content.Subject.ShouldBe(EmailTemplates.EmailConfirmationSubject);
        queued.Content.TextBody.ShouldContain("https://fitedu.test/confirm-email", Case.Sensitive);
    }

    [Fact]
    public async Task SendPasswordReset_QueuesTheResetInTheRequestedLanguage()
    {
        var queue = new EmailOutbox();

        await new QueuedEmailService(queue).SendPasswordResetAsync(Address, "https://fitedu.test/reset-password", UserLanguage.Greek);

        queue.Reader.TryRead(out var queued).ShouldBeTrue();
        queued!.Content.Subject.ShouldBe(EmailTemplates.GreekPasswordResetSubject);
    }

    [Fact]
    public async Task Send_WhenTheQueueIsFull_Throws_SoTheCallerCanLogTheFailure()
    {
        var queue = new EmailOutbox(capacity: 1);
        var service = new QueuedEmailService(queue);
        await service.SendPasswordResetAsync(Address, "https://fitedu.test/one", UserLanguage.English);

        await Should.ThrowAsync<InvalidOperationException>(
            service.SendPasswordResetAsync(Address, "https://fitedu.test/two", UserLanguage.English));
    }
}

using Microsoft.AspNetCore.Components;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Shared.Resources.Messages;

namespace FitEdu.Web.Pages;

/// <summary>
/// The page the link in the confirmation email opens: <c>/confirm-email?userId=…&amp;token=…</c> (built by
/// <c>AuthLinks</c>). When the member clicks, it confirms the two values in this process through
/// <see cref="IAuthApiService"/> (not over HTTP, where every member's click would count against the
/// server's own address in the "auth" rate limit).
/// </summary>
public partial class ConfirmEmail
{
    private const int BadRequestStatusCode = 400;
    private const int NotFoundStatusCode = 404;

    [Inject] private IAuthApiService AuthApiService { get; set; } = default!;
    [Inject] private ILogger<ConfirmEmail> Logger { get; set; } = default!;

    [SupplyParameterFromQuery] public string? UserId { get; set; }

    [SupplyParameterFromQuery] public string? Token { get; set; }

    private enum Step { Ready, Confirming, Confirmed, LinkInvalid }

    private Step step = Step.Ready;
    private string? errorMessage;

    // A link missing either value cannot be confirmed, so it shows the invalid-link state straight away.
    private Step CurrentStep => string.IsNullOrEmpty(UserId) || string.IsNullOrEmpty(Token) ? Step.LinkInvalid : step;

    private async Task Confirm()
    {
        step = Step.Confirming;
        errorMessage = null;

        try
        {
            var result = await AuthApiService.ConfirmEmailAsync(UserId!, Token!);

            if (result.IsSuccess)
            {
                step = Step.Confirmed;
            }
            else if (result.StatusCode is BadRequestStatusCode or NotFoundStatusCode)
            {
                // 400 means a used, expired or altered token and 404 an unknown member; either needs a new email.
                step = Step.LinkInvalid;
            }
            else
            {
                step = Step.Ready;
                errorMessage = ErrorMessagesRes.EmailConfirmationError;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogConfirmEmailFailed(Logger, ex);
            step = Step.Ready;
            errorMessage = ErrorMessagesRes.EmailConfirmationError;
        }
    }

    // Source-generated log methods (LoggerMessage) avoid the CA1848/CA1873 analyzer warnings.
    [LoggerMessage(Level = LogLevel.Error, Message = "Email confirmation request failed.")]
    private static partial void LogConfirmEmailFailed(ILogger logger, Exception exception);
}

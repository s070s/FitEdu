using System.Net;
using Microsoft.AspNetCore.Components;
using FitEdu.Application.DTOs;
using FitEdu.Shared.Resources.Messages;
using System.Net.Http.Json;

namespace FitEdu.Web.Pages;

/// <summary>
/// The page the link in the confirmation email opens: <c>/confirm-email?userId=…&amp;token=…</c> (built by
/// <c>AuthLinks</c>). It posts the two values to <c>api/auth/confirm-email</c> when the member clicks.
/// </summary>
public partial class ConfirmEmail
{
    private const string ConfirmEmailApiPath = "api/auth/confirm-email";

    [Inject] private IHttpClientFactory HttpClientFactory { get; set; } = default!;
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
            var http = HttpClientFactory.CreateClient("SelfClient");
            using var response = await http.PostAsJsonAsync(ConfirmEmailApiPath, new ConfirmEmailRequestDto { UserId = UserId!, Token = Token! });

            if (response.IsSuccessStatusCode)
            {
                step = Step.Confirmed;
            }
            else if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
            {
                // The API answers 400 for a used, expired or altered token; the member needs a new email.
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

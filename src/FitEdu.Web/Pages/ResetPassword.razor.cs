using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.DTOs;
using FitEdu.Shared.Resources.Messages;

namespace FitEdu.Web.Pages;

/// <summary>
/// The page the link in the password-reset email opens: <c>/reset-password?email=…&amp;token=…</c> (built by
/// <c>AuthLinks</c>). The member chooses a new password, which goes to <see cref="IAuthService.ResetPasswordAsync"/>
/// with the two values from the link.
/// </summary>
public partial class ResetPassword
{
    [Inject] private IAuthService AuthService { get; set; } = default!;
    [Inject] private ILogger<ResetPassword> Logger { get; set; } = default!;

    [SupplyParameterFromQuery] public string? Email { get; set; }

    [SupplyParameterFromQuery] public string? Token { get; set; }

    private enum Step { Ready, Done, LinkInvalid }

    private readonly ResetPasswordRequestDto resetRequest = new();
    private readonly List<string> errors = [];
    private Step step = Step.Ready;
    private bool isSaving;

    // A link without a usable email and token cannot reset anything, so it shows the invalid-link state straight away.
    private Step CurrentStep => HasLinkValues ? step : Step.LinkInvalid;

    private bool HasLinkValues =>
        !string.IsNullOrEmpty(Token) && !string.IsNullOrEmpty(Email) && new EmailAddressAttribute().IsValid(Email);

    protected override void OnParametersSet()
    {
        resetRequest.Email = Email ?? string.Empty;
        resetRequest.Token = Token ?? string.Empty;
    }

    private async Task HandleSubmit()
    {
        isSaving = true;
        errors.Clear();

        try
        {
            var result = await AuthService.ResetPasswordAsync(resetRequest);

            if (result.Success)
            {
                step = Step.Done;
            }
            else if (result.Failure == AuthFailure.LinkInvalid)
            {
                // A used, expired or altered link (or an unknown email): a new email is needed.
                step = Step.LinkInvalid;
            }
            else if (result.Errors is not null && result.Errors.Any())
            {
                // Only password-rule errors come back this way (the reset token was already accepted).
                errors.AddRange(result.Errors);
            }
            else
            {
                errors.Add(result.Message ?? ErrorMessagesRes.PasswordResetFailed);
            }
        }
        catch (Exception ex)
        {
            LogResetPasswordFailed(Logger, ex);
            errors.Add(ErrorMessagesRes.UnexpectedErrorTryAgain);
        }
        finally
        {
            isSaving = false;
        }
    }

    // Source-generated log methods (LoggerMessage) avoid the CA1848/CA1873 analyzer warnings.
    [LoggerMessage(Level = LogLevel.Error, Message = "Reset-password request failed.")]
    private static partial void LogResetPasswordFailed(ILogger logger, Exception exception);
}

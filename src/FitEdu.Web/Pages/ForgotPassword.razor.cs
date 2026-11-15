using Microsoft.AspNetCore.Components;
using FitEdu.Web.Interfaces;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.DTOs;
using FitEdu.Shared.Resources.Messages;

namespace FitEdu.Web.Pages;

public partial class ForgotPassword
{
    [Inject] private IAuthService AuthService { get; set; } = default!;
    [Inject] private ILogger<ForgotPassword> Logger { get; set; } = default!;

    private ForgotPasswordRequestDto forgotPasswordRequest = new();
    private string? successMessage;
    private string? errorMessage;
    private bool isLoading;

    private async Task HandleSubmit()
    {
        isLoading = true;
        successMessage = null;
        errorMessage = null;

        forgotPasswordRequest.Email = forgotPasswordRequest.Email?.Trim() ?? string.Empty;

        try
        {
            var result = await AuthService.ForgotPasswordAsync(forgotPasswordRequest);
            if (result.Success)
            {
                successMessage = result.Message ?? SuccessMessagesRes.PasswordResetLinkSent;
            }
            else
            {
                errorMessage = result.Message ?? ErrorMessagesRes.FailedToSendResetLink;
            }
        }
        catch (Exception ex)
        {
            LogForgotPasswordFailed(Logger, ex);
            errorMessage = ErrorMessagesRes.UnexpectedErrorTryAgain;
        }
        finally
        {
            isLoading = false;
        }
    }

    // Source-generated log methods (LoggerMessage) avoid the CA1848/CA1873 analyzer warnings.
    [LoggerMessage(Level = LogLevel.Error, Message = "Forgot-password request failed.")]
    private static partial void LogForgotPasswordFailed(ILogger logger, Exception exception);
}

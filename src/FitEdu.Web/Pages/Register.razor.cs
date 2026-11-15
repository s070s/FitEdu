using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Identity;
using Microsoft.JSInterop;
using FitEdu.Web.Interfaces;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Enums;
using FitEdu.Shared.Resources.Messages;

namespace FitEdu.Web.Pages;

public partial class Register : IDisposable
{
    private const string LoginPath = "/login";

    [Inject] private IAuthService AuthService { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private ILogger<Register> Logger { get; set; } = default!;
    [Inject] private IdentityErrorDescriber ErrorDescriber { get; set; } = default!;

    /// <summary>Where to go after the first log-in, e.g. the programme a guest was looking at.</summary>
    [SupplyParameterFromQuery] public string? ReturnUrl { get; set; }

    // RegisterRequest.DateOfBirth is now DateTime? default to 2000-01-01 as requested
    private RegisterRequestDto registerRequest = new() { DateOfBirth = new DateTime(2000, 1, 1) };
    private EditContext editContext = default!;
    private string? successMessage;
    private string? errorMessage;
    private List<string> errors = new();
    private bool isLoading;
    private bool showPassword;
    private bool showConfirmPassword;
    private string registeredEmail = string.Empty;
    private ElementReference errorRegion;
    private ElementReference successRegion;
    // A phone user taps Register at the bottom of a long form, far from where the result appears,
    // so the render after a submit scrolls the result (or the error above the button) into view.
    private bool scrollToFeedback;

    /// <summary>The log-in page, keeping the return address so the new member comes back to it.</summary>
    private string LoginHref => Login.LinkWithReturnUrl(LoginPath, ReturnUrl);

    private string passwordInputType => showPassword ? "text" : "password";
    private string passwordIconClass => showPassword ? "bi bi-eye-slash" : "bi bi-eye";
    private string confirmPasswordInputType => showConfirmPassword ? "text" : "password";
    private string confirmPasswordIconClass => showConfirmPassword ? "bi bi-eye-slash" : "bi bi-eye";

    // The optional goal select lists every goal; an empty value means "not sure yet".
    private static readonly FitnessGoal[] GoalOptions = Enum.GetValues<FitnessGoal>();

    protected override void OnInitialized()
    {
        editContext = new EditContext(registerRequest);
        editContext.OnFieldChanged += HandleFieldChanged;
    }

    private void HandleFieldChanged(object? sender, FieldChangedEventArgs e)
    {
        successMessage = null;
        errorMessage = null;
        errors.Clear();

        if (e.FieldIdentifier.FieldName == nameof(RegisterRequestDto.Password))
        {
            editContext.NotifyFieldChanged(editContext.Field(nameof(RegisterRequestDto.ConfirmPassword)));
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!scrollToFeedback)
            return;

        scrollToFeedback = false;
        var feedbackRegion = string.IsNullOrEmpty(successMessage) ? errorRegion : successRegion;
        try
        {
            await JS.InvokeVoidAsync("inputInterop.scrollIntoView", feedbackRegion);
        }
        catch (JSDisconnectedException)
        {
            // The circuit closed; there is no page left to scroll.
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        editContext.OnFieldChanged -= HandleFieldChanged;
    }

    private void TogglePasswordVisibility()
    {
        showPassword = !showPassword;
    }

    private void ToggleConfirmPasswordVisibility()
    {
        showConfirmPassword = !showConfirmPassword;
    }

    private async Task HandleRegister()
    {
        isLoading = true;
        successMessage = null;
        errorMessage = null;
        errors.Clear();

        // Trim the text inputs, but not the passwords: they are sent exactly as typed, as the sign-in and the API take them.
        registerRequest.Email = registerRequest.Email?.Trim() ?? string.Empty;
        registerRequest.FirstName = registerRequest.FirstName?.Trim() ?? string.Empty;
        registerRequest.LastName = registerRequest.LastName?.Trim() ?? string.Empty;

        registeredEmail = registerRequest.Email;
        try
        {
            var result = await AuthService.RegisterAsync(registerRequest);
            ShowResult(result);
        }
        catch (Exception ex)
        {
            // The form and what the visitor typed stay, so a second try needs no retyping.
            LogRegisterFailed(Logger, ex);
            errorMessage = ErrorMessagesRes.UnexpectedErrorTryAgain;
        }
        finally
        {
            isLoading = false;
        }

        scrollToFeedback = true;
    }

    private void ShowResult(AuthResponseDto result)
    {
        if (result.Success)
        {
            // Always our own translated text, never the server's message.
            successMessage = SuccessMessagesRes.RegistrationSuccess;
            registerRequest = new()
            {
                DateOfBirth = new DateTime(2000, 1, 1)
            }; // Reset, keep default DOB
            editContext.OnFieldChanged -= HandleFieldChanged;
            editContext = new EditContext(registerRequest);
            editContext.OnFieldChanged += HandleFieldChanged;
        }
        else
        {
            errorMessage = result.Message;
            if (result.Errors != null)
            {
                errors = result.Errors.ToList();

                // Matched on the describer's own (translated) text, so it works in every language.
                var emailTakenText = ErrorDescriber.DuplicateEmail(registeredEmail).Description;
                if (errors.Contains(emailTakenText))
                {
                    // The short translated message says it all.
                    errorMessage = ErrorMessagesRes.EmailTakenError;
                    errors.Clear();
                }
            }

            // A refusal always says something, so the visitor is never left with a form that does nothing.
            if (string.IsNullOrEmpty(errorMessage) && errors.Count == 0)
                errorMessage = ErrorMessagesRes.RegistrationFailedGeneric;
        }
    }

    // Source-generated log methods (LoggerMessage) avoid the CA1848/CA1873 analyzer warnings.
    // The message names no email address.
    [LoggerMessage(Level = LogLevel.Error, Message = "Unexpected error during sign-up.")]
    private static partial void LogRegisterFailed(ILogger logger, Exception exception);
}

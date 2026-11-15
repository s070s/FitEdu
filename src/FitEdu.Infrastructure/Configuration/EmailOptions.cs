using Microsoft.Extensions.Options;

namespace FitEdu.Infrastructure.Configuration;

/// <summary>Binds the "Email" configuration section. Secrets (Username/Password) come from
/// User Secrets / environment variables, never committed appsettings.</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public SmtpOptions Smtp { get; set; } = new();
}

public sealed class SmtpOptions
{
    public const int DefaultTimeoutSeconds = 15;
    public const int MaxTimeoutSeconds = 300;

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "no-reply@fitedu.com";
    public string FromName { get; set; } = "FitEdu";

    /// <summary>The longest the relay may take to answer one SMTP command (connect, sign in, send), in
    /// seconds. MailKit's own default is two minutes, which would keep a send stuck for that long.</summary>
    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;
}

/// <summary>Checks the "Email" section at startup (CFG-4), so a typo fails then and not at the first send.</summary>
public sealed class EmailOptionsValidator : IValidateOptions<EmailOptions>
{
    public ValidateOptionsResult Validate(string? name, EmailOptions options)
    {
        var smtp = options.Smtp;
        if (smtp.Port is < 1 or > 65535)
            return ValidateOptionsResult.Fail("Email:Smtp:Port must be between 1 and 65535.");

        if (smtp.TimeoutSeconds is < 1 or > SmtpOptions.MaxTimeoutSeconds)
            return ValidateOptionsResult.Fail($"Email:Smtp:TimeoutSeconds must be between 1 and {SmtpOptions.MaxTimeoutSeconds}.");

        return ValidateOptionsResult.Success;
    }
}

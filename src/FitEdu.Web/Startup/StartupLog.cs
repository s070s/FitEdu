namespace FitEdu.Web.Startup;

/// <summary>
/// Source-generated log methods (LoggerMessage) for the startup code that cannot be a partial
/// class (top-level Program.cs and static extension classes). They avoid the CA1848/CA1873
/// analyzer warnings; the logger passed in keeps the original category.
/// </summary>
internal static partial class StartupLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception. TraceId: {TraceId}")]
    internal static partial void UnhandledException(ILogger logger, Exception? exception, string traceId);

    [LoggerMessage(Level = LogLevel.Information, Message = "FitEdu.Web started. Logs: {LogsPath}")]
    internal static partial void Started(ILogger logger, string logsPath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Startup migrate/seed failed on attempt {Attempt}/{MaxAttempts} — retrying in {Delay}.")]
    internal static partial void MigrateSeedRetrying(ILogger logger, Exception exception, int attempt, int maxAttempts, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Startup migrate/seed failed after {MaxAttempts} attempts — the application cannot start.")]
    internal static partial void MigrateSeedFailed(ILogger logger, Exception exception, int maxAttempts);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded admin user '{Email}'.")]
    internal static partial void AdminSeeded(ILogger logger, string email);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Failed to seed admin user '{Email}': {Errors}")]
    internal static partial void AdminSeedFailed(ILogger logger, string email, string errors);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A request reached the app from a loopback address without X-Forwarded-For. Loopback callers are never rate limited, so if a reverse proxy on this host forwards visitors it must send X-Forwarded-For (nginx: proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;); otherwise every visitor looks like loopback and the rate limits do nothing. Logged once per process.")]
    internal static partial void LoopbackWithoutForwardedFor(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Data Protection keys are protected only by Windows DPAPI: this Windows account on this machine can read them and nothing else can. Restoring the database onto another host or account loses every sign-in session and every encrypted assessment answer. Set DataProtection:CertificatePath (with DataProtection:CertificatePassword) or DataProtection:CertificateThumbprint to protect them with a certificate you back up offline.")]
    internal static partial void DataProtectionKeysDpapiOnly(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Data Protection keys are stored in the database unencrypted: anyone who can read the database can read them, and with them forge sign-in cookies and decrypt the assessment answers. Set DataProtection:CertificatePath (with DataProtection:CertificatePassword) or DataProtection:CertificateThumbprint to protect them with a certificate you back up offline.")]
    internal static partial void DataProtectionKeysUnencrypted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Auth completion: token invalid or expired.")]
    internal static partial void AuthTokenInvalid(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Auth completion: token had unexpected format.")]
    internal static partial void AuthTokenBadFormat(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Auth completion: token had unexpected format.")]
    internal static partial void AuthTokenEmpty(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Auth completion: ticket was empty or already used — refused.")]
    internal static partial void AuthTicketReused(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Auth completion: user {UserId} not found.")]
    internal static partial void AuthUserNotFound(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Auth completion: security stamp mismatch for user {UserId} — token invalidated.")]
    internal static partial void AuthStampMismatch(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} signed in via /auth/complete.")]
    internal static partial void AuthSignedIn(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Auth completion: user {UserId} is disabled — no session issued.")]
    internal static partial void AuthUserDisabled(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Exit impersonation refused: {Reason}.")]
    internal static partial void AuthExitRefused(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Admin {AdminId} left the impersonation and is signed in again.")]
    internal static partial void AuthExitedImpersonation(ILogger logger, string adminId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Admin {AdminId} is impersonating user {UserId}.")]
    internal static partial void AuthImpersonating(ILogger logger, string? adminId, string userId);
}

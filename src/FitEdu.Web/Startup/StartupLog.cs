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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Auth completion: token invalid or expired.")]
    internal static partial void AuthTokenInvalid(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Auth completion: token had unexpected format.")]
    internal static partial void AuthTokenBadFormat(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Auth completion: token had unexpected format.")]
    internal static partial void AuthTokenEmpty(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Auth completion: user {UserId} not found.")]
    internal static partial void AuthUserNotFound(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Auth completion: security stamp mismatch for user {UserId} — token invalidated.")]
    internal static partial void AuthStampMismatch(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Auth completion: GetUserTierAsync failed for {UserId} — defaulting to Free.")]
    internal static partial void AuthTierLookupFailed(ILogger logger, Exception exception, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} signed in via /auth/complete.")]
    internal static partial void AuthSignedIn(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Admin {AdminId} is impersonating user {UserId}.")]
    internal static partial void AuthImpersonating(ILogger logger, string? adminId, string userId);
}

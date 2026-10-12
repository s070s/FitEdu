using Microsoft.Extensions.Logging;

namespace FitEdu.TestSupport;

/// <summary>
/// Keeps the text of every warning and error so a test can check what the code under test reported.
/// </summary>
public sealed class RecordingLogger : ILogger
{
    public List<string> Warnings { get; } = [];

    public List<string> Errors { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        if (logLevel == LogLevel.Warning)
        {
            Warnings.Add(message);
            return;
        }

        if (logLevel >= LogLevel.Error)
            Errors.Add(exception is null ? message : $"{message} ({exception.Message})");
    }
}

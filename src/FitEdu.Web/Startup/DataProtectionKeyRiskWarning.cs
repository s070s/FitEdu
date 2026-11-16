namespace FitEdu.Web.Startup;

/// <summary>
/// Logs ONE warning at startup when the Data Protection keys have no certificate (see
/// <see cref="DataProtectionKeyProtection"/>), so the owner learns the risk instead of finding it out at a
/// restore. Registered only outside Development and only without a certificate. Startup is not refused:
/// the demo deployment runs without one on purpose.
/// </summary>
public sealed class DataProtectionKeyRiskWarning(ILogger<DataProtectionKeyRiskWarning> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
            StartupLog.DataProtectionKeysDpapiOnly(logger);
        else
            StartupLog.DataProtectionKeysUnencrypted(logger);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

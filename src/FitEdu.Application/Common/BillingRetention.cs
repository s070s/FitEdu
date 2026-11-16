namespace FitEdu.Application.Common;

/// <summary>
/// How long billing records are kept. A record belongs to the financial year it was made in, which is
/// the calendar year in Europe/Athens (the Greek tax year; not <c>Site:TimeZone</c>, which a host may
/// change). It is kept for <see cref="RetentionYearsAfterFinancialYear"/> full years after the end of
/// that year, then deleted by <c>BillingRetentionPurgeService</c>. The period is the minimum the Greek
/// bookkeeping law asks for; the owner may change it here.
/// </summary>
public static class BillingRetention
{
    public const int RetentionYearsAfterFinancialYear = 5;

    private const string FinancialYearTimeZoneId = "Europe/Athens";

    private static readonly TimeZoneInfo FinancialYearTimeZone = TimeZoneInfo.FindSystemTimeZoneById(FinancialYearTimeZoneId);

    /// <summary>
    /// The moment before which a billing record is past its retention: 1 January of (this year minus
    /// <see cref="RetentionYearsAfterFinancialYear"/>) at 00:00 in Athens, as UTC. For 2026 that is
    /// 2021-01-01 00:00 in Athens, 2020-12-31T22:00Z. Records created before it belong to a financial
    /// year that ended at least five full years ago; the purge deletes <c>CreatedAt &lt; cutoff</c>.
    /// </summary>
    public static DateTimeOffset CutoffUtc(DateTimeOffset now)
    {
        var currentYear = TimeZoneInfo.ConvertTime(now, FinancialYearTimeZone).Year;
        var cutoffYear = currentYear - RetentionYearsAfterFinancialYear;
        var startOfCutoffYear = new DateTime(cutoffYear, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(startOfCutoffYear, FinancialYearTimeZone), TimeSpan.Zero);
    }
}

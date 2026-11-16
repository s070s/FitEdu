using FitEdu.Application.Common;
using Shouldly;
using Xunit;

namespace FitEdu.Application.Tests;

/// <summary>
/// The billing retention cutoff counts the financial year (the calendar year in Europe/Athens) in the
/// Athens zone whatever the zone of the server or of Site:TimeZone, so each test names its moments in UTC.
/// </summary>
public class BillingRetentionTests
{
    // 1 January 2021 00:00 in Athens (UTC+2 in winter) is 31 December 2020 22:00 UTC.
    private static readonly DateTimeOffset CutoffDuring2026 = new(2020, 12, 31, 22, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RetentionIsFiveFullYearsAfterTheFinancialYear()
    {
        BillingRetention.RetentionYearsAfterFinancialYear.ShouldBe(5);
    }

    [Fact]
    public void CutoffUtc_During2026_IsTheStartOf2021InAthens()
    {
        var cutoff = BillingRetention.CutoffUtc(new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.Zero));

        cutoff.ShouldBe(CutoffDuring2026);
        cutoff.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void CutoffUtc_InSummerAndWinterOf2026_IsTheSame()
    {
        // 1 January is always in winter time, so the summer-time offset of "now" must not leak into the cutoff.
        BillingRetention.CutoffUtc(new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero)).ShouldBe(CutoffDuring2026);
        BillingRetention.CutoffUtc(new DateTimeOffset(2026, 12, 1, 12, 0, 0, TimeSpan.Zero)).ShouldBe(CutoffDuring2026);
    }

    [Fact]
    public void CutoffUtc_OneMinuteBeforeAthensNewYear_StillBelongsToTheOldYear()
    {
        // 21:59 UTC on 31 December 2025 is 23:59 in Athens: still 2025, so the cutoff is the start of 2020.
        var cutoff = BillingRetention.CutoffUtc(new DateTimeOffset(2025, 12, 31, 21, 59, 0, TimeSpan.Zero));

        cutoff.ShouldBe(new DateTimeOffset(2019, 12, 31, 22, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void CutoffUtc_AtAthensNewYear_MovesToTheNextYear()
    {
        // 22:00 UTC on 31 December 2025 is midnight in Athens: 2026 has begun, so the cutoff is the start of 2021.
        var cutoff = BillingRetention.CutoffUtc(new DateTimeOffset(2025, 12, 31, 22, 0, 0, TimeSpan.Zero));

        cutoff.ShouldBe(CutoffDuring2026);
    }

    [Fact]
    public void CutoffUtc_ReadsTheMomentNotTheOffsetItWasGivenIn()
    {
        var sameMomentInAthens = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(2));
        var sameMomentInNewYork = new DateTimeOffset(2025, 12, 31, 17, 0, 0, TimeSpan.FromHours(-5));

        BillingRetention.CutoffUtc(sameMomentInAthens).ShouldBe(CutoffDuring2026);
        BillingRetention.CutoffUtc(sameMomentInNewYork).ShouldBe(CutoffDuring2026);
    }
}

using System.Globalization;

namespace FitEdu.Web.Extensions;

/// <summary>
/// Formats a price in its own currency with the reader's number layout: "€9.99" in English and
/// "9,99 €" in Greek. The culture's own currency symbol is replaced, because en-GB would
/// otherwise print a euro price as "£9.99".
/// </summary>
public static class MoneyFormatter
{
    public const string DefaultCurrencyCode = "EUR";

    private const char NoBreakSpace = '\u00A0';

    private static readonly Dictionary<string, string> SymbolsByCurrencyCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EUR"] = "€",
        ["GBP"] = "£",
        ["USD"] = "$"
    };

    public static string Format(decimal amount, string? currencyCode = DefaultCurrencyCode, CultureInfo? culture = null)
    {
        var formatCulture = culture ?? CultureInfo.CurrentCulture;
        var numberFormat = (NumberFormatInfo)formatCulture.NumberFormat.Clone();
        numberFormat.CurrencySymbol = Symbol(currencyCode);
        // A no-break space keeps "9,99 €" on one line (ICU already uses one, Windows NLS does not).
        return amount.ToString("C", numberFormat).Replace(' ', NoBreakSpace);
    }

    /// <summary>The currency's sign, or its ISO code when it has no known sign.</summary>
    public static string Symbol(string? currencyCode)
    {
        var code = string.IsNullOrWhiteSpace(currencyCode) ? DefaultCurrencyCode : currencyCode.Trim();
        return SymbolsByCurrencyCode.TryGetValue(code, out var symbol) ? symbol : code.ToUpperInvariant();
    }
}

using System.Globalization;

namespace FitEdu.TestSupport;

/// <summary>
/// Saves the current culture and UI culture and puts both back on <see cref="Dispose"/>, so a test that
/// switches language cannot leak it into the tests that run after it on the same thread.
/// </summary>
public sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _originalUICulture = CultureInfo.CurrentUICulture;

    /// <summary>Only saves the current cultures; use it when the test switches culture itself.</summary>
    public CultureScope()
    {
    }

    /// <summary>Saves the current cultures, then switches both to the named culture (e.g. "el" or "en-GB").</summary>
    public CultureScope(string cultureName)
        : this(new CultureInfo(cultureName))
    {
    }

    /// <summary>Saves the current cultures, then switches both to the given culture.</summary>
    public CultureScope(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _originalCulture;
        CultureInfo.CurrentUICulture = _originalUICulture;
    }
}

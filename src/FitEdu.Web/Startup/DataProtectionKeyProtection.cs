using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;

namespace FitEdu.Web.Startup;

/// <summary>
/// How the Data Protection key ring is protected at rest (P03-2). The ring lives in the database;
/// it signs the auth cookies and encrypts the assessment answers. With a certificate from
/// configuration the keys are encrypted with it on every OS. Without one, Windows falls back to DPAPI
/// (readable only by this Windows account on this machine) and Linux stores the keys unencrypted;
/// <see cref="DataProtectionKeyRiskWarning"/> says so in the log outside Development.
/// </summary>
public static class DataProtectionKeyProtection
{
    public const string CertificatePathSetting = "DataProtection:CertificatePath";

    public const string CertificatePasswordSetting = "DataProtection:CertificatePassword";

    public const string CertificateThumbprintSetting = "DataProtection:CertificateThumbprint";

    /// <summary>
    /// The configured key certificate: a .pfx file (<see cref="CertificatePathSetting"/> with
    /// <see cref="CertificatePasswordSetting"/>) or a certificate with a private key in the CurrentUser or
    /// LocalMachine "My" store (<see cref="CertificateThumbprintSetting"/>); null when none is configured.
    /// Throws when the configuration is wrong, so a typo stops startup instead of silently dropping the protection.
    /// </summary>
    public static X509Certificate2? LoadCertificate(IConfiguration config)
    {
        var path = config[CertificatePathSetting];
        var thumbprint = config[CertificateThumbprintSetting];
        var hasPath = !string.IsNullOrWhiteSpace(path);
        var hasThumbprint = !string.IsNullOrWhiteSpace(thumbprint);

        if (hasPath && hasThumbprint)
            throw new InvalidOperationException($"Set {CertificatePathSetting} or {CertificateThumbprintSetting}, not both.");
        if (hasPath)
            return LoadFromFile(path!, config[CertificatePasswordSetting]);
        if (hasThumbprint)
            return FindInStores(thumbprint!);

        return null;
    }

    /// <summary>Encrypts the keys with the certificate, or else with DPAPI on Windows (as before this setting existed).</summary>
    public static void Protect(IDataProtectionBuilder builder, X509Certificate2? certificate)
    {
        if (certificate is not null)
            builder.ProtectKeysWithCertificate(certificate);
        else if (OperatingSystem.IsWindows())
            builder.ProtectKeysWithDpapi();
    }

    private static X509Certificate2 LoadFromFile(string path, string? password)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException($"{CertificatePathSetting} points to a file that does not exist: '{path}'.");

        X509Certificate2 certificate;
        try
        {
            // Ephemeral: the private key stays in memory, so no key file is left in a user profile
            // (an IIS app pool often has none loaded).
            certificate = X509CertificateLoader.LoadPkcs12FromFile(path, password, X509KeyStorageFlags.EphemeralKeySet);
        }
        catch (CryptographicException error)
        {
            throw new InvalidOperationException(
                $"{CertificatePathSetting} could not be opened as a .pfx (PKCS #12) file. Check the file and {CertificatePasswordSetting}.", error);
        }

        if (!certificate.HasPrivateKey)
        {
            certificate.Dispose();
            throw new InvalidOperationException($"The certificate in {CertificatePathSetting} has no private key; the keys could not be decrypted.");
        }

        return certificate;
    }

    private static X509Certificate2 FindInStores(string thumbprint)
    {
        // Spaces and the invisible character a copy from the certificate dialog brings along are not part of a thumbprint.
        var cleaned = new string(thumbprint.Where(char.IsAsciiHexDigit).ToArray());

        foreach (var location in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
        {
            var found = FindWithPrivateKey(location, cleaned);
            if (found is not null)
                return found;
        }

        throw new InvalidOperationException(
            $"{CertificateThumbprintSetting} names no certificate with a private key in the CurrentUser or LocalMachine \"My\" store.");
    }

    private static X509Certificate2? FindWithPrivateKey(StoreLocation location, string thumbprint)
    {
        try
        {
            using var store = new X509Store(StoreName.My, location);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            // Not only valid ones: a self-signed key certificate is not trusted, and need not be.
            var matches = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);
            return matches.FirstOrDefault(candidate => candidate.HasPrivateKey);
        }
        catch (CryptographicException)
        {
            // The store does not exist on this machine (common on Linux).
            return null;
        }
    }
}

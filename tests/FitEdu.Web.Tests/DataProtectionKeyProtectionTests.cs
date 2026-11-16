using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FitEdu.Infrastructure.Data;
using FitEdu.TestSupport;
using FitEdu.Web.Startup;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>A self-signed certificate with a private key, saved as a .pfx in its own temp folder that Dispose removes.</summary>
public sealed class TestKeyCertificate : IDisposable
{
    public const string Password = "test-pfx-password-1!";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fitedu-tests-" + Guid.NewGuid().ToString("N"));

    public string PfxPath { get; }

    public string Thumbprint { get; }

    /// <param name="useEcdsaKey">Makes the private key an ECDSA key, which the Data Protection key ring cannot use.</param>
    public TestKeyCertificate(bool useEcdsaKey = false)
    {
        Directory.CreateDirectory(_folder);
        using var rsa = RSA.Create(2048);
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = useEcdsaKey
            ? new CertificateRequest("CN=FitEdu test keys", ecdsa, HashAlgorithmName.SHA256)
            : new CertificateRequest("CN=FitEdu test keys", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        PfxPath = Path.Combine(_folder, "keys.pfx");
        File.WriteAllBytes(PfxPath, certificate.Export(X509ContentType.Pfx, Password));
        Thumbprint = certificate.Thumbprint;
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }
}

/// <summary>A Production host whose key ring is protected with a generated test certificate.</summary>
public class ProductionWithKeyCertificateWebAppFactory : ProductionWebAppFactory
{
    private readonly TestKeyCertificate _certificate = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting(DataProtectionKeyProtection.CertificatePathSetting, _certificate.PfxPath);
        builder.UseSetting(DataProtectionKeyProtection.CertificatePasswordSetting, TestKeyCertificate.Password);
        base.ConfigureWebHost(builder);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _certificate.Dispose();
    }
}

/// <summary>
/// P03-2: the key ring can be protected with a certificate from configuration on every OS; without
/// one, Windows keeps DPAPI and a non-Development start logs one warning naming the risk.
/// </summary>
public class DataProtectionKeyProtectionTests : IClassFixture<ProductionWithKeyCertificateWebAppFactory>, IClassFixture<ProductionWebAppFactory>
{
    private readonly ProductionWithKeyCertificateWebAppFactory _withCertificate;
    private readonly ProductionWebAppFactory _withoutCertificate;

    public DataProtectionKeyProtectionTests(
        ProductionWithKeyCertificateWebAppFactory withCertificate, ProductionWebAppFactory withoutCertificate)
    {
        _withCertificate = withCertificate;
        _withoutCertificate = withoutCertificate;
    }

    private static IConfiguration ConfigWith(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build();

    [Fact]
    public void LoadCertificate_NothingConfigured_IsNull()
    {
        DataProtectionKeyProtection.LoadCertificate(ConfigWith()).ShouldBeNull();
    }

    [Fact]
    public void LoadCertificate_PfxFileAndPassword_LoadsTheCertificateWithItsPrivateKey()
    {
        using var testCertificate = new TestKeyCertificate();

        using var loaded = DataProtectionKeyProtection.LoadCertificate(ConfigWith(
            (DataProtectionKeyProtection.CertificatePathSetting, testCertificate.PfxPath),
            (DataProtectionKeyProtection.CertificatePasswordSetting, TestKeyCertificate.Password)));

        loaded.ShouldNotBeNull().Thumbprint.ShouldBe(testCertificate.Thumbprint);
        loaded.HasPrivateKey.ShouldBeTrue();
    }

    [Fact]
    public void LoadCertificate_MissingFile_FailsAndNamesTheSetting()
    {
        var missing = Path.Combine(Path.GetTempPath(), "fitedu-missing-" + Guid.NewGuid().ToString("N"), "keys.pfx");

        var error = Should.Throw<InvalidOperationException>(() =>
            DataProtectionKeyProtection.LoadCertificate(ConfigWith((DataProtectionKeyProtection.CertificatePathSetting, missing))));

        error.Message.ShouldContain(DataProtectionKeyProtection.CertificatePathSetting);
    }

    [Fact]
    public void LoadCertificate_WrongPassword_FailsWithoutRepeatingIt()
    {
        using var testCertificate = new TestKeyCertificate();
        const string wrongPassword = "not-the-password-9?";

        var error = Should.Throw<InvalidOperationException>(() => DataProtectionKeyProtection.LoadCertificate(ConfigWith(
            (DataProtectionKeyProtection.CertificatePathSetting, testCertificate.PfxPath),
            (DataProtectionKeyProtection.CertificatePasswordSetting, wrongPassword))));

        error.Message.ShouldContain(DataProtectionKeyProtection.CertificatePasswordSetting);
        error.Message.ShouldNotContain(wrongPassword);
        error.Message.ShouldNotContain(TestKeyCertificate.Password);
    }

    [Fact]
    public void LoadCertificate_FileThatIsNotACertificate_Fails()
    {
        using var testCertificate = new TestKeyCertificate();
        var notACertificate = Path.Combine(Path.GetDirectoryName(testCertificate.PfxPath)!, "notes.txt");
        File.WriteAllText(notACertificate, "not a certificate");

        Should.Throw<InvalidOperationException>(() => DataProtectionKeyProtection.LoadCertificate(
            ConfigWith((DataProtectionKeyProtection.CertificatePathSetting, notACertificate))));
    }

    [Fact]
    public void LoadCertificate_ThumbprintNotInTheStores_FailsAndNamesTheSetting()
    {
        var error = Should.Throw<InvalidOperationException>(() => DataProtectionKeyProtection.LoadCertificate(
            ConfigWith((DataProtectionKeyProtection.CertificateThumbprintSetting, new string('A', 40)))));

        error.Message.ShouldContain(DataProtectionKeyProtection.CertificateThumbprintSetting);
    }

    [Fact]
    public void LoadCertificate_FileAndThumbprintTogether_Fails()
    {
        using var testCertificate = new TestKeyCertificate();

        var error = Should.Throw<InvalidOperationException>(() => DataProtectionKeyProtection.LoadCertificate(ConfigWith(
            (DataProtectionKeyProtection.CertificatePathSetting, testCertificate.PfxPath),
            (DataProtectionKeyProtection.CertificateThumbprintSetting, testCertificate.Thumbprint))));

        error.Message.ShouldContain("not both");
    }

    [Fact]
    public void StartingTheHost_WithAMissingCertificateFile_Fails()
    {
        var missing = Path.Combine(Path.GetTempPath(), "fitedu-missing-" + Guid.NewGuid().ToString("N"), "keys.pfx");
        using var app = _withoutCertificate.WithWebHostBuilder(builder =>
            builder.UseSetting(DataProtectionKeyProtection.CertificatePathSetting, missing));

        var error = Should.Throw<InvalidOperationException>(() => app.CreateClient());

        error.Message.ShouldContain(DataProtectionKeyProtection.CertificatePathSetting);
    }

    [Fact]
    public void LoadCertificate_CertificateWithAnEcdsaKey_FailsNamingTheRsaKey()
    {
        using var ecdsaCertificate = new TestKeyCertificate(useEcdsaKey: true);

        var error = Should.Throw<InvalidOperationException>(() => DataProtectionKeyProtection.LoadCertificate(ConfigWith(
            (DataProtectionKeyProtection.CertificatePathSetting, ecdsaCertificate.PfxPath),
            (DataProtectionKeyProtection.CertificatePasswordSetting, TestKeyCertificate.Password))));

        error.Message.ShouldContain("cannot open its RSA private key");
    }

    [Fact]
    public void StartingTheHost_WithACertificateThatHasAnEcdsaKey_Fails()
    {
        using var ecdsaCertificate = new TestKeyCertificate(useEcdsaKey: true);
        using var app = _withoutCertificate.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(DataProtectionKeyProtection.CertificatePathSetting, ecdsaCertificate.PfxPath);
            builder.UseSetting(DataProtectionKeyProtection.CertificatePasswordSetting, TestKeyCertificate.Password);
        });

        var error = Should.Throw<InvalidOperationException>(() => app.CreateClient());

        error.Message.ShouldContain("cannot open its RSA private key");
    }

    [Fact]
    public void Host_WithAThumbprintWrittenWithSpaces_EncryptsTheKeysWithTheStoreCertificate()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var testCertificate = new TestKeyCertificate();
        // PersistKeySet: a key that only lives in memory would not survive being added to the store.
        using var storeCertificate = X509CertificateLoader.LoadPkcs12FromFile(
            testCertificate.PfxPath, TestKeyCertificate.Password, X509KeyStorageFlags.PersistKeySet);
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        store.Add(storeCertificate);
        try
        {
            var spaced = string.Join(' ', Enumerable.Range(0, testCertificate.Thumbprint.Length / 2)
                .Select(index => testCertificate.Thumbprint.Substring(index * 2, 2)));
            using var app = _withoutCertificate.WithWebHostBuilder(builder =>
                builder.UseSetting(DataProtectionKeyProtection.CertificateThumbprintSetting, spaced));

            app.Services.GetRequiredService<IOptions<KeyManagementOptions>>().Value
                .XmlEncryptor.ShouldBeOfType<CertificateXmlEncryptor>();
        }
        finally
        {
            store.Remove(storeCertificate);
            // Removing the certificate leaves its key file in the user profile; delete it too.
            using var rsa = storeCertificate.GetRSAPrivateKey();
            if (rsa is RSACng cng)
                cng.Key.Delete();
        }
    }

    [Fact]
    public void Host_MovedFromDpapiToACertificate_StillReadsDataProtectedBefore()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var payload = "p03-2-dpapi-" + Guid.NewGuid().ToString("N");
        string protectedPayload;
        using (var firstHost = _withoutCertificate.WithWebHostBuilder(_ => { }))
        {
            protectedPayload = firstHost.Services.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("P03-2").Protect(payload);
        }

        using var testCertificate = new TestKeyCertificate();
        using var secondHost = _withoutCertificate.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(DataProtectionKeyProtection.CertificatePathSetting, testCertificate.PfxPath);
            builder.UseSetting(DataProtectionKeyProtection.CertificatePasswordSetting, TestKeyCertificate.Password);
        });

        secondHost.Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("P03-2").Unprotect(protectedPayload).ShouldBe(payload);
    }

    [Fact]
    public void Host_WithACertificate_EncryptsTheKeysWithIt()
    {
        var options = _withCertificate.Services.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        options.XmlEncryptor.ShouldBeOfType<CertificateXmlEncryptor>();
    }

    [Fact]
    public async Task Host_WithACertificate_StoresTheKeysEncryptedAndReadsThemBack()
    {
        var payload = "p03-2-" + Guid.NewGuid().ToString("N");
        var protectedPayload = _withCertificate.Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("P03-2").Protect(payload);

        using var scope = _withCertificate.Services.CreateScope();
        var storedKeys = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .DataProtectionKeys.Select(key => key.Xml).ToListAsync();
        storedKeys.ShouldNotBeEmpty();
        storedKeys.ShouldAllBe(xml => xml!.Contains("EncryptedData") && !xml.Contains("DPAPI"));

        // A second host over the same database has no cached keys, so it must decrypt the stored ones with the certificate.
        using var secondHost = _withCertificate.WithWebHostBuilder(_ => { });
        secondHost.Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("P03-2").Unprotect(protectedPayload).ShouldBe(payload);
    }

    private static (RecordingLogger<DataProtectionKeyRiskWarning> Log, IDisposable App) HostRecordingTheKeyWarning(
        ProductionWebAppFactory factory)
    {
        var log = new RecordingLogger<DataProtectionKeyRiskWarning>();
        var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<ILogger<DataProtectionKeyRiskWarning>>(log)));
        _ = app.Services;
        return (log, app);
    }

    [Fact]
    public void ProductionHost_WithoutACertificate_LogsOneWarningNamingTheRisk()
    {
        var (log, app) = HostRecordingTheKeyWarning(_withoutCertificate);
        using var _ = app;

        log.Warnings.Count.ShouldBe(1);
        log.Warnings[0].ShouldContain(DataProtectionKeyProtection.CertificatePathSetting);
    }

    [Fact]
    public void ProductionHost_WithACertificate_LogsNoWarning()
    {
        var (log, app) = HostRecordingTheKeyWarning(_withCertificate);
        using var _ = app;

        log.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void DevelopmentHost_WithoutACertificate_LogsNoWarning()
    {
        var log = new RecordingLogger<DataProtectionKeyRiskWarning>();
        using var developmentFactory = new CustomWebAppFactory();
        using var app = developmentFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<ILogger<DataProtectionKeyRiskWarning>>(log)));
        _ = app.Services;

        log.Warnings.ShouldBeEmpty();
    }
}

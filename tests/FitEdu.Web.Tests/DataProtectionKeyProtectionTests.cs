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

    public TestKeyCertificate()
    {
        Directory.CreateDirectory(_folder);
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=FitEdu test keys", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
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

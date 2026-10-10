using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class TlsCertificateSelectorTests
{
    public static void FixtureCertificatePinRejectsUnexpectedMissingExpiredAndWrongNameCertificates()
    {
        using var temp = new CertificatePinFixtureDirectory();
        var path = Path.Combine(temp.Path, "expected.pfx");
        TestCertificates.WriteSelfSignedPfx(path, "pin.test");
        using var expected = X509CertificateLoader.LoadPkcs12FromFile(path, password: null, X509KeyStorageFlags.EphemeralKeySet);
        using var unrelated = Certificate("pin.test");
        var validate = TestCertificates.PinServerCertificate(path);
        var sender = new object();
        AssertEx.True(validate(sender, expected, chain: null, SslPolicyErrors.RemoteCertificateChainErrors));
        AssertEx.False(validate(sender, unrelated, chain: null, SslPolicyErrors.RemoteCertificateChainErrors));
        AssertEx.False(validate(sender, certificate: null, chain: null, SslPolicyErrors.RemoteCertificateNotAvailable));
        AssertEx.False(validate(sender, expected, chain: null, SslPolicyErrors.RemoteCertificateNameMismatch));
        var selectionOnly = TestCertificates.PinServerCertificate(path, allowNameMismatch: true);
        AssertEx.True(selectionOnly(sender, expected, chain: null, SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateChainErrors));
        AssertEx.False(selectionOnly(sender, unrelated, chain: null, SslPolicyErrors.RemoteCertificateNameMismatch));
        // Overwriting fixture storage never changes an already captured certificate pin.
        TestCertificates.WriteSelfSignedPfx(path, "pin.test");
        using var replaced = X509CertificateLoader.LoadPkcs12FromFile(path, password: null, X509KeyStorageFlags.EphemeralKeySet);
        AssertEx.True(validate(sender, expected, chain: null, SslPolicyErrors.RemoteCertificateChainErrors));
        AssertEx.False(validate(sender, replaced, chain: null, SslPolicyErrors.RemoteCertificateChainErrors));
        var expiredPath = Path.Combine(temp.Path, "expired.pfx");
        File.WriteAllBytes(expiredPath, TestCertificates.CreateSelfSignedPfxBytesForValidity("pin.test", password: null,
            DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow.AddDays(-5)));
        using var expired = X509CertificateLoader.LoadPkcs12FromFile(expiredPath, password: null, X509KeyStorageFlags.EphemeralKeySet);
        AssertEx.False(TestCertificates.PinServerCertificate(expiredPath)(sender, expired, chain: null, SslPolicyErrors.RemoteCertificateChainErrors));
        var futurePath = Path.Combine(temp.Path, "future.pfx");
        File.WriteAllBytes(futurePath, TestCertificates.CreateSelfSignedPfxBytesForValidity("pin.test", password: null,
            DateTimeOffset.UtcNow.AddDays(5), DateTimeOffset.UtcNow.AddDays(10)));
        using var future = X509CertificateLoader.LoadPkcs12FromFile(futurePath, password: null, X509KeyStorageFlags.EphemeralKeySet);
        AssertEx.False(TestCertificates.PinServerCertificate(futurePath)(sender, future, chain: null, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    public static void RuntimeCertificateFactoryBuildsManualAndAcmeCertificates()
    {
        using var manualCertificate = Certificate("manual.test");
        using var acmeCertificate = Certificate("acme.test");
        var domains = new List<string>
        {
            "acme.test"
        };
        var manual = RuntimeCertificateFactory.ManualPfx("manual", "certs/manual.pfx", hasConfiguredPassword: true, manualCertificate);
        var acme = RuntimeCertificateFactory.Acme("acme", acmeCertificate, domains);
        domains[0] = "replacement.test";
        domains.Clear();
        AssertEx.Equal("manual", manual.Id);
        AssertEx.Equal("certs/manual.pfx", manual.Path);
        AssertEx.Equal(RuntimeCertificateFactory.PfxFormat, manual.Format);
        AssertEx.True(manual.HasConfiguredPassword);
        AssertEx.Equal(RuntimeCertificateFactory.ManualPfxSource, manual.Source);
        AssertEx.Equal(0, manual.Domains.Count);
        AssertEx.Equal("acme", acme.Id);
        AssertEx.Equal("acme://acme", acme.Path);
        AssertEx.Equal(RuntimeCertificateFactory.PfxFormat, acme.Format);
        AssertEx.False(acme.HasConfiguredPassword);
        AssertEx.Equal(RuntimeCertificateFactory.AcmeSource, acme.Source);
        AssertEx.Equal("acme.test", acme.Domains[0]);
        AssertEx.Throws<ArgumentException>(() => new RuntimeCertificate(null!, "certs/manual.pfx", "pfx", HasConfiguredPassword: false, manualCertificate, "manualPfx", []));
        AssertEx.Throws<ArgumentException>(() => new RuntimeCertificate("manual", " ", "pfx", HasConfiguredPassword: false, manualCertificate, "manualPfx", []));
        AssertEx.Throws<ArgumentException>(() => new RuntimeCertificate("manual", "certs/manual.pfx", "", HasConfiguredPassword: false, manualCertificate, "manualPfx", []));
        AssertEx.Throws<ArgumentException>(() => new RuntimeCertificate("manual", "certs/manual.pfx", "pfx", HasConfiguredPassword: false, manualCertificate, "\t", []));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeCertificate("manual", "certs/manual.pfx", "pfx", HasConfiguredPassword: false, null!, "manualPfx", []));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeCertificate("manual", "certs/manual.pfx", "pfx", HasConfiguredPassword: false, manualCertificate, "manualPfx", null!));
        AssertEx.Throws<ArgumentNullException>(() => new RuntimeCertificate("manual", "certs/manual.pfx", "pfx", HasConfiguredPassword: false, manualCertificate, "manualPfx", [null!]));
    }

    public static void SelectionInputReadsRuntimeFactsWithoutSnapshot()
    {
        using var defaultCertificate = Certificate("default.test");
        var certificates = new Dictionary<string, RuntimeCertificate>(StringComparer.OrdinalIgnoreCase)
        {
            ["default"] = RuntimeCertificate("default", defaultCertificate)
        };
        var bindings = new List<RuntimeSniCertificateBinding>
        {
            new RuntimeSniCertificateBinding("home.test", "default")
        };
        var input = TlsCertificateSelectionInputMapper.FromSources(certificates.Select(static certificate => certificate), "default", bindings.Select(static binding => binding), "Home.Test");
        certificates.Clear();
        bindings.Clear();
        AssertEx.True(input.Certificates.ContainsKey("default"));
        AssertEx.Equal("default", input.DefaultCertificateId);
        AssertEx.Equal(1, input.SniCertificates.Count);
        AssertEx.Equal("home.test", input.SniCertificates[0].HostName);
        AssertEx.Equal("Home.Test", input.HostName);
        AssertEx.False(input.SniCertificates is RuntimeSniCertificateBinding[], "TLS selection SNI certificates should not expose a mutable array.");
        AssertEx.Throws<ArgumentNullException>(() => TlsCertificateSelectionInputMapper.FromSources(null!, "default", [], "home.test"));
        AssertEx.Throws<ArgumentNullException>(() => TlsCertificateSelectionInputMapper.FromSources(certificates, "default", null!, "home.test"));
        AssertEx.Throws<ArgumentNullException>(() => TlsCertificateSelectionInputMapper.FromSources([new KeyValuePair<string, RuntimeCertificate>(null!, RuntimeCertificate("broken", defaultCertificate))], "default", [], "home.test"));
        AssertEx.Throws<ArgumentNullException>(() => TlsCertificateSelectionInputMapper.FromSources([new KeyValuePair<string, RuntimeCertificate>("broken", null!)], "default", [], "home.test"));
        AssertEx.Throws<ArgumentNullException>(() => TlsCertificateSelectionInputMapper.FromSources(certificates, "default", [null!], "home.test"));
    }

    public static void SelectsSniCertificateBeforeDefaultAndFallsBackSafely()
    {
        using var defaultCertificate = Certificate("default.test");
        using var homeCertificate = Certificate("home.test");
        var certificates = new Dictionary<string, RuntimeCertificate>(StringComparer.OrdinalIgnoreCase)
        {
            ["default"] = RuntimeCertificate("default", defaultCertificate),
            ["home"] = RuntimeCertificate("home", homeCertificate)
        };
        var bindings = new[]
        {
            new RuntimeSniCertificateBinding("Home.Test", "home"),
            new RuntimeSniCertificateBinding("missing.test", "missing")
        };
        var selectedSni = TlsCertificateSelector.SelectCertificate(new TlsCertificateSelectionInput(certificates, "default", bindings, "home.test"));
        var selectedFallback = TlsCertificateSelector.SelectCertificate(new TlsCertificateSelectionInput(certificates, "default", bindings, "unknown.test"));
        var selectedMissingSniFallback = TlsCertificateSelector.SelectCertificate(new TlsCertificateSelectionInput(certificates, "default", bindings, "missing.test"));
        var selectedNone = TlsCertificateSelector.SelectCertificate(new TlsCertificateSelectionInput(certificates, "missing", bindings, "absent.test"));
        AssertEx.Equal(homeCertificate.Thumbprint, AssertEx.NotNull(selectedSni).Thumbprint);
        AssertEx.Equal(defaultCertificate.Thumbprint, AssertEx.NotNull(selectedFallback).Thumbprint);
        AssertEx.Equal(defaultCertificate.Thumbprint, AssertEx.NotNull(selectedMissingSniFallback).Thumbprint);
        AssertEx.True(selectedNone is null);
    }

    private static X509Certificate2 Certificate(string subjectName)
    {
        return X509CertificateLoader.LoadPkcs12(TestCertificates.CreateSelfSignedPfxBytes(subjectName), null);
    }

    private static RuntimeCertificate RuntimeCertificate(string id, X509Certificate2 certificate)
    {
        return new RuntimeCertificate(id, $"{id}.pfx", "pfx", false, certificate, "manualPfx", []);
    }

    private static RuntimeListener Listener(string? defaultCertificateId, IReadOnlyList<RuntimeSniCertificateBinding> sniCertificates)
    {
        return new RuntimeListener("main", "127.0.0.1", 8443, true, RuntimeListenerTransport.Https, defaultCertificateId, sniCertificates, 512, 32 * 1024, 32 * 1024, 1024, 64 * 1024);
    }

    private static ProxyConfigurationSnapshot Snapshot(IReadOnlyDictionary<string, RuntimeCertificate> certificates, IReadOnlyList<RuntimeListener> listeners)
    {
        return new ProxyConfigurationSnapshot(1, DateTimeOffset.UnixEpoch, "test", ["site.json"], new ProxyConfigurationDiscovery(new ProxyFilesystemLayout("test", "test/config", "test/config/sites", "test/logs", "test/certs", "test/state", "test/config/proxy.json"), [], [], []), new RuntimeAdminSecurityOptions([], true, true, null, "MDRAVA_ADMIN_TOKEN", "none", 100), new RuntimeAcmeOptions(false, true, "", [], false, "acme", 30, 720, 60, []), new RuntimeTimeouts(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10)), new RuntimeConnectionLimits(100, 16, 1024), new RuntimeObservabilityOptions(true, 100, new RuntimeLogPersistenceOptions(true, true, 1_048_576, 8)), new RuntimeLimits(4096, 128, 240, 30, 32768, 128, 8192, 104857600, 8192, TimeSpan.FromSeconds(15)), new RuntimeForwardedHeadersOptions(true, []), certificates, listeners, []);
    }
}

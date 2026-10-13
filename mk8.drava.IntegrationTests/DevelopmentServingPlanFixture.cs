using Mk8.Drava.Application.INF.Publication;
using Mk8.Drava.Configuration;
using Supprocom.Secrets;

namespace Mk8.Drava.IntegrationTests;

internal sealed class DevelopmentServingPlanFixture : IDisposable
{
    private readonly string _directory;
    private string? _configurationDocument;
    public PlanClock Clock { get; } = new();
    public LocalSiteCertificateAuthority Authority { get; }
    public ApplicationBootstrap Application { get; }
    public GatewayBootstrap Gateway { get; }

    private DevelopmentServingPlanFixture(string directory, PlanClock clock, LocalSiteCertificateAuthority authority, string fingerprint)
    {
        _directory = directory; Clock = clock; Authority = authority;
        Application = new ApplicationBootstrap { IpcToken = new SecretText(new string('A', 64)), SiteId = "site", StateDirectory = directory, Controller = new ControllerBootstrap
            { Domain = "site.test", CertificateAuthority = authority.PrivateMaterial, EnrollmentRootFingerprint = fingerprint, DnsServerAddress = "127.0.0.1", }, };
        Gateway = new GatewayBootstrap { ApplicationToken = Application.IpcToken, SiteId = "site", StateDirectory = directory, EnrollmentRootFingerprint = fingerprint };
    }

    public static Task<DevelopmentServingPlanFixture> CreateAsync()
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "drava_plan_" + Guid.NewGuid().ToString("N"))).FullName;
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var clock = new PlanClock();
        try
        {
            var authority = LocalSiteCertificateAuthority.Create("site", clock);
            try { return Task.FromResult(new DevelopmentServingPlanFixture(directory, clock, authority, authority.Fingerprint)); }
            catch { authority.Dispose(); throw; }
        }
        catch { Directory.Delete(directory, recursive: true); throw; }
    }

    public async Task<PortableRole<ApplicationBootstrap>> OpenRoleAsync(ApplicationBootstrap? bootstrap = null)
    {
        var configuration = bootstrap ?? Application;
        var document = BootstrapFile.Serialize(configuration);
        var path = Path.Combine(_directory, ".env");
        if (_configurationDocument is null)
        {
            await PortableFixtureFile.WriteAsync(path, PortableConfiguration.SerializeDocument<ApplicationBootstrap>(document)).ConfigureAwait(false);
        }
        else if (!string.Equals(document, _configurationDocument, StringComparison.Ordinal))
        {
            // A fresh startup snapshot changes only configuration, retaining the accepted material slot.
            var store = new SupprocomSecretFileStore(new SupprocomSecretFileOptions
            {
                Directory = _directory, Protection = SecretFileProtection.InstallationBoundAesGcm,
            }, "Production");
            await store.SetAsync("Drava:ApplicationBootstrap", document, CancellationToken.None).ConfigureAwait(false);
        }
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(false);
        _configurationDocument = document;
        return role;
    }

    public void Dispose() { Authority.Dispose(); Directory.Delete(_directory, recursive: true); }

    internal sealed class PlanClock : TimeProvider
    {
        private DateTimeOffset _utc = DateTimeOffset.UtcNow;
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public override DateTimeOffset GetUtcNow() => _utc;
        public void Advance(TimeSpan elapsed) { _timestamp += elapsed.Ticks; _utc += elapsed; }
        public void AdjustUtc(TimeSpan change) => _utc += change;
    }
}

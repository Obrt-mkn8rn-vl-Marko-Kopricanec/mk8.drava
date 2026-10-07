using Mk8.Drava.Application.INF.Publication;
using Mk8.Drava.Configuration;

namespace Mk8.Drava.IntegrationTests;

internal sealed class DevelopmentServingPlanFixture : IDisposable
{
    private readonly string _directory;
    public PlanClock Clock { get; } = new();
    public LocalSiteCertificateAuthority Authority { get; }
    public ApplicationBootstrap Application { get; }
    public GatewayBootstrap Gateway { get; }

    private DevelopmentServingPlanFixture(string directory, PlanClock clock, LocalSiteCertificateAuthority authority, string fingerprint)
    {
        _directory = directory; Clock = clock; Authority = authority;
        Application = new ApplicationBootstrap { SiteId = "site", StateDirectory = directory, Controller = new ControllerBootstrap
            { Domain = "site.test", CertificateAuthorityPath = Path.Combine(directory, "ca.pfx"), EnrollmentRootFingerprint = fingerprint } };
        Gateway = new GatewayBootstrap { SiteId = "site", StateDirectory = directory, EnrollmentRootFingerprint = fingerprint };
    }

    public static async Task<DevelopmentServingPlanFixture> CreateAsync()
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "drava_plan_" + Guid.NewGuid().ToString("N"))).FullName;
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var clock = new PlanClock();
        var path = Path.Combine(directory, "ca.pfx");
        try
        {
            var fingerprint = await LocalSiteCertificateAuthority.InitializeAsync(path, "site", clock, CancellationToken.None).ConfigureAwait(false);
            return new DevelopmentServingPlanFixture(directory, clock, LocalSiteCertificateAuthority.Open(path, fingerprint, clock), fingerprint);
        }
        catch { Directory.Delete(directory, recursive: true); throw; }
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

using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Mk8.Drava.Application.DAL.Publication;
using Mk8.Drava.Configuration;
using Mk8.Drava.Transport.Protocol.V1;

namespace Mk8.Drava.IntegrationTests;

internal sealed class DevelopmentInitializedSite : IAsyncDisposable
{
    private readonly string _parent;
    private DevelopmentProcess? _application;
    private DevelopmentProcess? _gateway;
    private string? _retainedApplicationPath;
    private string? _applicationBeforeRestartLog;
    private string? _applicationBeforeStopLog;
    public string DirectoryPath { get; }
    public string OptionsPath { get; }
    public string EnrollmentPath => Path.Combine(DirectoryPath, "roles", "node", "Environment", ".env");
    public string ApplicationPath => _application?.PortableDocumentPath ?? _retainedApplicationPath
        ?? Path.Combine(DirectoryPath, "roles", "application", "Environment", ".env");
    public string GatewayPath => Path.Combine(DirectoryPath, "roles", "gateway", "Environment", ".env");
    public string? InitializationLog { get; private set; }
    public JsonObject Options { get; }

    public DevelopmentInitializedSite(int dnsPort = 53)
    {
        _parent = Path.Combine(Path.GetTempPath(), "dri_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_parent);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(_parent, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        DirectoryPath = Path.Combine(_parent, "site");
        OptionsPath = Path.Combine(_parent, "initialize.json");
        Options = new JsonObject
        {
            ["destinationDirectory"] = DirectoryPath, ["siteId"] = "development", ["domain"] = "site.test", ["ownerId"] = "development",
            ["httpPort"] = TwoProcessProxy.UnusedPort(), ["httpsPort"] = TwoProcessProxy.UnusedPort(),
            ["registrationPort"] = TwoProcessProxy.UnusedPort(), ["managementPort"] = TwoProcessProxy.UnusedPort(),
            ["discoveryEnabled"] = false, ["dnsServerAddress"] = "127.0.0.1", ["dnsServerPort"] = dnsPort,
            ["registration"] = new JsonObject
            {
                ["leaseSeconds"] = 15, ["renewAfterSeconds"] = 4, ["readinessIntervalMilliseconds"] = 250,
                ["readinessTimeoutMilliseconds"] = 1000, ["readinessValiditySeconds"] = 30, ["readinessSuccesses"] = 1,
                ["readinessFailures"] = 1, ["reconcileIntervalMilliseconds"] = 100,
            },
        };
    }

    public async Task<int> InitializeAsync(string? rawJson = null)
    {
        await PortableFixtureFile.WriteAsync(OptionsPath, PortableConfiguration.SerializeDocument<Mk8.Drava.Application.Installation.SiteInitializationOptions>(rawJson ?? Options.ToJsonString())).ConfigureAwait(false);
        var command = new DevelopmentProcess(Assembly("mk8.drava.Application"), OptionsPath, "--initialize-site");
        await using var lifetime = command.ConfigureAwait(false);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        try
        {
            return await command.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
        }
        finally
        {
            await command.DisposeAsync().ConfigureAwait(false);
            InitializationLog = command.CapturedLog;
        }
    }

    public async Task StartAsync()
    {
        _application = new DevelopmentProcess(Assembly("mk8.drava.Application"), ApplicationPath);
        _gateway = new DevelopmentProcess(Assembly("mk8.drava.Gateway"), GatewayPath);
        var profile = await PortableConfiguration.LoadDocumentAsync<NodeEnrollmentProfile>(EnrollmentPath, CancellationToken.None).ConfigureAwait(false);
        var gateway = await PortableConfiguration.LoadDocumentAsync<GatewayBootstrap>(GatewayPath, CancellationToken.None).ConfigureAwait(false);
        using var client = new DevelopmentSiteClient(profile.Site.RootCertificate, gateway.HttpsPort, "probe.site.test");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        while (true)
        {
            _application.ThrowIfExited(); _gateway.ThrowIfExited();
            try
            {
                using var response = await client.Client.GetAsync(new Uri("/_drava/live", UriKind.Relative), deadline.Token).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.OK) return;
            }
            catch (HttpRequestException) { }
            await Task.Delay(100, deadline.Token).ConfigureAwait(false);
        }
    }

    public async Task<PresentationPlan> ReadCurrentApplicationPlanAsync()
    {
        var application = _application ?? throw new InvalidOperationException("Application has not started.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        while (true)
        {
            application.ThrowIfExited();
            var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(application.PortableDocumentPath, deadline.Token).ConfigureAwait(false);
            await using (role.ConfigureAwait(false))
            {
                var bytes = new GatewayMaterialStore(role).Read();
                if (bytes is not null)
                {
                    try { return PresentationPlan.Parser.ParseFrom(bytes); }
                    finally { CryptographicOperations.ZeroMemory(bytes); }
                }
            }
            await Task.Delay(100, deadline.Token).ConfigureAwait(false);
        }
    }

    public async Task RestartApplicationAsync()
    {
        var application = _application ?? throw new InvalidOperationException("Application has not started.");
        var retained = await application.RetirePortableAsync(Path.Combine(_parent, "application-installation")).ConfigureAwait(false);
        _applicationBeforeRestartLog ??= application.CapturedLog;
        _retainedApplicationPath = retained;
        _application = null;
        _application = new DevelopmentProcess(Assembly("mk8.drava.Application"), retained);
    }

    public async Task StopApplicationAsync()
    {
        var application = _application ?? throw new InvalidOperationException("Application has not started.");
        _retainedApplicationPath = await application.RetirePortableAsync(Path.Combine(_parent, "application-installation")).ConfigureAwait(false);
        _applicationBeforeStopLog ??= application.CapturedLog;
        _application = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_gateway is not null) await _gateway.DisposeAsync().ConfigureAwait(false);
        if (_application is not null) await _application.DisposeAsync().ConfigureAwait(false);
        var evidence = System.IO.Directory.CreateDirectory(Path.Combine(TwoProcessProxy.FindRoot(), "artifacts", "site-initialization-tests", Path.GetFileName(_parent))).FullName;
        await File.WriteAllTextAsync(Path.Combine(evidence, "initialization.log"), InitializationLog).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(evidence, "application.log"), _application?.CapturedLog).ConfigureAwait(false);
        if (_applicationBeforeRestartLog is not null)
            await File.WriteAllTextAsync(Path.Combine(evidence, "application-before-restart.log"), _applicationBeforeRestartLog).ConfigureAwait(false);
        if (_applicationBeforeStopLog is not null)
            await File.WriteAllTextAsync(Path.Combine(evidence, "application-before-stop.log"), _applicationBeforeStopLog).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(evidence, "gateway.log"), _gateway?.CapturedLog).ConfigureAwait(false);
        System.IO.Directory.Delete(_parent, recursive: true);
    }

    private static string Assembly(string project) => DevelopmentBinaryPaths.ForProject(project);
}

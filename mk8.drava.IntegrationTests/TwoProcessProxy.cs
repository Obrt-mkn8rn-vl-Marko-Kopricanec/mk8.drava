using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Mk8.Drava.Configuration;
using Mk8.Drava.Application.INF.Publication;
using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.DAL.Registry;
using Mk8.Drava.Application.DAL.Acme;

namespace Mk8.Drava.IntegrationTests;

internal sealed class TwoProcessProxy : IAsyncDisposable
{
    private readonly string _directory;
    private DevelopmentProcess? _application;
    private DevelopmentProcess _gateway;
    public int Port { get; }
    public IpcEndpoint Ipc { get; }
    public HttpClient Client { get; }
    public int TlsPort { get; private set; }
    public int RegistrationPort { get; private set; }
    public int ManagementPort { get; private set; }
    public string AdministratorTokenPath => Path.Combine(_directory, "app", "administrator.token");
    public string RootCertificatePath => Path.Combine(_directory, "site-root.der");
    public string NodeCertificatePath => Path.Combine(_directory, "node.pfx");
    public string EnrolledNodeId { get; private set; } = "local";
    public string NodeRelayAddress { get; private set; } = "127.0.0.1";
    public Task WritePolicyAsync(string json) => File.WriteAllTextAsync(Path.Combine(_directory, "app", "config", "noconf.json"), json);

    public async Task RestartAsync()
    {
        await _gateway.DisposeAsync().ConfigureAwait(false);
        if (_application is not null) await _application.DisposeAsync().ConfigureAwait(false);
        var evidence = Directory.CreateDirectory(Path.Combine(FindRoot(), "artifacts", "noconf-tests", Path.GetFileName(_directory))).FullName;
        await File.WriteAllTextAsync(Path.Combine(evidence, "gateway-before-restart.log"), _gateway.CapturedLog).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(evidence, "application-before-restart.log"), _application?.CapturedLog ?? "").ConfigureAwait(false);
        _application = new DevelopmentProcess(DevelopmentBinaryPaths.ForProject("mk8.drava.Application"), Path.Combine(_directory, "application.json"));
        _gateway = new DevelopmentProcess(DevelopmentBinaryPaths.ForProject("mk8.drava.Gateway"), Path.Combine(_directory, "gateway.json"));
        await WaitUntilBoundAsync().ConfigureAwait(false);
    }
    public string ApplicationPlanPath => Path.Combine(_directory, "app", "gateway-serving.plan");
    public string GatewayPlanPath => Path.Combine(_directory, "gateway", "serving.plan");
    internal string GatewayLog => _gateway.CapturedLog;

    public async Task StopApplicationAsync()
    {
        if (_application is null) return;
        await _application.DisposeAsync().ConfigureAwait(false);
        var evidence = Directory.CreateDirectory(Path.Combine(FindRoot(), "artifacts", "recovery-tests", Path.GetFileName(_directory))).FullName;
        await File.WriteAllTextAsync(Path.Combine(evidence, "application-before-stop.log"), _application.CapturedLog).ConfigureAwait(false);
        _application = null;
    }

    public async Task StartApplicationAsync()
    {
        if (_application is not null) throw new InvalidOperationException("Development Application is already started.");
        _application = new DevelopmentProcess(DevelopmentBinaryPaths.ForProject("mk8.drava.Application"), Path.Combine(_directory, "application.json"));
        await WaitUntilBoundAsync().ConfigureAwait(false);
    }

    public async Task RestartGatewayAsync()
    {
        await _gateway.DisposeAsync().ConfigureAwait(false);
        var evidence = Directory.CreateDirectory(Path.Combine(FindRoot(), "artifacts", "recovery-tests", Path.GetFileName(_directory))).FullName;
        await File.WriteAllTextAsync(Path.Combine(evidence, "gateway-before-restart.log"), _gateway.CapturedLog).ConfigureAwait(false);
        _gateway = new DevelopmentProcess(DevelopmentBinaryPaths.ForProject("mk8.drava.Gateway"), Path.Combine(_directory, "gateway.json"));
        await WaitUntilBoundAsync().ConfigureAwait(false);
    }

    public Task WriteManualRouteAsync(int upstreamPort, int? listenerPort = null) => File.WriteAllTextAsync(Path.Combine(_directory, "app", "config", "sites", "service.json"), JsonSerializer.Serialize(new
    {
        name = "test", host = "app.test", listeners = new[] { new { name = "http", address = "127.0.0.1", port = listenerPort ?? Port } },
        pathPrefix = "/", upstreams = new[] { new { name = "test", address = "127.0.0.1", port = upstreamPort } },
    }));

    private TwoProcessProxy(string directory, int port, IpcEndpoint ipc, DevelopmentProcess? application, DevelopmentProcess gateway)
    {
        _directory = directory;
        Port = port;
        Ipc = ipc;
        _application = application;
        _gateway = gateway;
        Client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = TimeSpan.FromSeconds(15) };
        Client.DefaultRequestHeaders.Host = "app.test";
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "The factory transfers both process instances to the returned IAsyncDisposable fixture. Every failed construction/start path disposes them; successful fixture disposal kills and joins both child processes before deleting state.")]
    public static async Task<TwoProcessProxy> StartAsync(int upstreamPort, string host = "app.test", bool enrolledSite = false, bool manualRoute = true, int? dnsPort = null, bool discovery = false, bool administration = false, bool relayNode = false, bool startApplication = true, DevelopmentLifecycleSettings? lifecycleSettings = null, bool upstreamHttp2 = false, bool manualCache = false, bool upstreamHttp3 = false, IReadOnlyList<GatewayRequestBodyLimit>? requestBodyLimits = null, long? routeBodyLimit = null, int? responseHeadTimeoutMs = null, UpstreamTlsOptions? verifiedUpstreamTls = null, DevelopmentFlowTimeoutSettings? flowTimeouts = null)
    {
        if (upstreamHttp2 && upstreamHttp3) throw new ArgumentException("Development upstream protocol must be unambiguous.", nameof(upstreamHttp3));
        if (flowTimeouts is not null && (!manualRoute || upstreamHttp2 || upstreamHttp3 || verifiedUpstreamTls is not null ||
            manualCache || routeBodyLimit is not null || responseHeadTimeoutMs is not null))
            throw new ArgumentException("Development flow settings own two HTTP/1 manual routes and cannot replace overlapping configurations.", nameof(flowTimeouts));
        var directory = Path.Combine(Path.GetTempPath(), "drava_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var state = Directory.CreateDirectory(Path.Combine(directory, "app")).FullName;
        var gatewayState = Directory.CreateDirectory(Path.Combine(directory, "gateway")).FullName;
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(state, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var token = Path.Combine(directory, "gateway.token");
        await File.WriteAllTextAsync(token, Convert.ToHexString(RandomNumberGenerator.GetBytes(32))).ConfigureAwait(false);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(token, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var ipc = OperatingSystem.IsWindows() ? new IpcEndpoint { NamedPipeName = "drava_" + Guid.NewGuid().ToString("N"), IdentityTokenPath = token }
            : new IpcEndpoint { UnixSocketPath = Path.Combine(state, "app.sock"), IdentityTokenPath = token };
        var port = UnusedPort();
        var registrationPort = UnusedPort();
        var tlsPort = enrolledSite ? UnusedPort() : 0;
        var managementPort = administration ? UnusedPort() : 0;
        var relayAddress = relayNode ? LocalRelayAddress() : null;
        var (applicationPath, gatewayPath) = await WriteBootstrapAsync(directory, state, gatewayState, ipc, port, tlsPort, registrationPort, managementPort, upstreamPort, host, enrolledSite, manualRoute, dnsPort, discovery, relayAddress, lifecycleSettings ?? new DevelopmentLifecycleSettings(), upstreamHttp2, manualCache, upstreamHttp3, requestBodyLimits, routeBodyLimit, responseHeadTimeoutMs, verifiedUpstreamTls, flowTimeouts).ConfigureAwait(false);
        var application = startApplication ? new DevelopmentProcess(DevelopmentBinaryPaths.ForProject("mk8.drava.Application"), applicationPath) : null;
        DevelopmentProcess? gateway = null;
        try
        {
            gateway = new DevelopmentProcess(DevelopmentBinaryPaths.ForProject("mk8.drava.Gateway"), gatewayPath);
            var result = new TwoProcessProxy(directory, port, ipc, application, gateway) { TlsPort = tlsPort, RegistrationPort = registrationPort, ManagementPort = managementPort,
                EnrolledNodeId = relayNode ? "remote" : "local", NodeRelayAddress = relayAddress ?? "127.0.0.1" };
            try { await result.WaitUntilBoundAsync().ConfigureAwait(false); return result; }
            catch { result.Client.Dispose(); throw; }
        }
        catch (Exception exception)
        {
            if (gateway is not null) await gateway.DisposeAsync().ConfigureAwait(false);
            if (application is not null) await application.DisposeAsync().ConfigureAwait(false);
            await CaptureStartupFailureAsync(directory, application, gateway, exception).ConfigureAwait(false);
            Directory.Delete(directory, recursive: true);
            throw;
        }
    }

    private static async Task CaptureStartupFailureAsync(string directory, DevelopmentProcess? application, DevelopmentProcess? gateway, Exception exception)
    {
        var evidence = Directory.CreateDirectory(Path.Combine(FindRoot(), "artifacts", "noconf-tests", Path.GetFileName(directory))).FullName;
        await File.WriteAllTextAsync(Path.Combine(evidence, "application-startup-failed.log"), application?.CapturedLog ?? "not started").ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(evidence, "gateway-startup-failed.log"), gateway?.CapturedLog ?? "not started").ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(evidence, "startup-failure.log"), exception.ToString()).ConfigureAwait(false);
    }

    private static async Task<(string Application, string Gateway)> WriteBootstrapAsync(string directory, string state, string gatewayState, IpcEndpoint ipc, int port, int tlsPort, int registrationPort, int managementPort, int upstreamPort, string host, bool enrolledSite, bool manualRoute, int? dnsPort, bool discovery, string? relayAddress, DevelopmentLifecycleSettings lifecycleSettings, bool upstreamHttp2, bool manualCache, bool upstreamHttp3, IReadOnlyList<GatewayRequestBodyLimit>? requestBodyLimits, long? routeBodyLimit, int? responseHeadTimeoutMs, UpstreamTlsOptions? verifiedUpstreamTls, DevelopmentFlowTimeoutSettings? flowTimeouts)
    {
        ControllerBootstrap? controller = null;
        if (enrolledSite)
        {
            var authorityPath = Path.Combine(state, "site-ca.pfx");
            var fingerprint = await LocalSiteCertificateAuthority.InitializeAsync(authorityPath, "development", TimeProvider.System, CancellationToken.None).ConfigureAwait(false);
            using var authority = LocalSiteCertificateAuthority.Open(authorityPath, fingerprint, TimeProvider.System);
            using var rootCertificate = authority.PublicCertificate;
            await File.WriteAllBytesAsync(Path.Combine(directory, "site-root.der"), rootCertificate.RawData).ConfigureAwait(false);
            await EnrollDevelopmentNodeAsync(directory, state, authority, relayAddress).ConfigureAwait(false);
            controller = new ControllerBootstrap { Domain = "site.test", CertificateAuthorityPath = authorityPath, EnrollmentRootFingerprint = fingerprint, RegistrationPort = registrationPort, DnsServerAddress = dnsPort.HasValue ? "127.0.0.1" : "", DnsServerPort = dnsPort ?? 53, Registration = lifecycleSettings.Registration, ServingPlan = lifecycleSettings.Serving };
        }
        var administratorPath = managementPort > 0 ? Path.Combine(state, "administrator.token") : "";
        if (administratorPath.Length > 0)
        {
            await File.WriteAllTextAsync(administratorPath, Convert.ToHexString(RandomNumberGenerator.GetBytes(32))).ConfigureAwait(false);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(administratorPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        var applicationBootstrap = new ApplicationBootstrap { SiteId = "development", NodeId = "local", StateDirectory = state, Listen = ipc, HttpPort = port, HttpsPort = tlsPort, ManagementPort = managementPort, AdministratorTokenPath = administratorPath, Controller = controller };
        var gatewayBootstrap = new GatewayBootstrap { SiteId = "development", StateDirectory = gatewayState, Application = ipc, HttpPort = port, HttpsPort = tlsPort, ManagementPort = managementPort, RegistrationPort = registrationPort, DiscoveryEnabled = discovery, Plan = lifecycleSettings.Gateway, EnrollmentRootFingerprint = controller?.EnrollmentRootFingerprint ?? "", RequestBodyLimits = requestBodyLimits ?? [] };
        var sites = Directory.CreateDirectory(Path.Combine(state, "config", "sites")).FullName;
        if (manualRoute) await File.WriteAllTextAsync(Path.Combine(sites, "service.json"), JsonSerializer.Serialize(new
        {
            name = "test", host, listeners = new[] { new { name = "http", address = "127.0.0.1", port } },
            pathPrefix = "/", upstreams = new[] { new { name = "test", address = "127.0.0.1", port = upstreamPort,
                scheme = upstreamHttp2 || upstreamHttp3 || verifiedUpstreamTls is not null ? "https" : "http", protocol = upstreamHttp2 ? "http2" : upstreamHttp3 ? "http3" : "http1",
                upstreamTls = new { validateCertificate = verifiedUpstreamTls?.ValidateCertificate ?? (!upstreamHttp2 && !upstreamHttp3),
                    sniHost = verifiedUpstreamTls?.SniHost ?? (upstreamHttp2 || upstreamHttp3 ? "upstream.test" : ""), trustedRoot = verifiedUpstreamTls?.TrustedRoot } } },
            overrides = new { maxRequestBodyBytes = routeBodyLimit, upstreamResponseHeadTimeoutMs = responseHeadTimeoutMs },
            cache = new { enabled = manualCache, maxEntryBytes = 4096, maxTotalBytes = 8192, defaultTtlSeconds = 60, respectOriginCacheControl = true },
        })).ConfigureAwait(false);
        if (flowTimeouts is not null)
            await WriteFlowSettingsAsync(state, sites, host, port, upstreamPort, flowTimeouts).ConfigureAwait(false);
        var applicationPath = Path.Combine(directory, "application.json");
        var gatewayPath = Path.Combine(directory, "gateway.json");
        await File.WriteAllTextAsync(applicationPath, BootstrapFile.Serialize(applicationBootstrap)).ConfigureAwait(false);
        await File.WriteAllTextAsync(gatewayPath, BootstrapFile.Serialize(gatewayBootstrap)).ConfigureAwait(false);
        return (applicationPath, gatewayPath);
    }

    private static async Task WriteFlowSettingsAsync(string state, string sites, string host, int port, int upstreamPort, DevelopmentFlowTimeoutSettings settings)
    {
        await File.WriteAllTextAsync(Path.Combine(state, "config", "proxy.json"), JsonSerializer.Serialize(new ProxyOperationalOptions { Timeouts = settings.Global })).ConfigureAwait(false);
        var origin = new UpstreamOptions { Name = "origin", Address = "127.0.0.1", Port = upstreamPort };
        var site = new SiteOptions
        {
            Name = "flow", Host = host,
            Listeners = [new ListenerOptions { Name = "http", Address = "127.0.0.1", Port = port }],
            Routes = [new ProxyRouteOptions { Name = "events", Host = host, PathPrefix = "/events", Overrides = settings.Events, Upstreams = [origin] },
                new ProxyRouteOptions { Name = "ordinary", Host = host, PathPrefix = "/", Upstreams = [origin] }],
        };
        await File.WriteAllTextAsync(Path.Combine(sites, "service.json"), JsonSerializer.Serialize(site)).ConfigureAwait(false);
    }

    private static async Task EnrollDevelopmentNodeAsync(string directory, string state, LocalSiteCertificateAuthority authority, string? relayAddress)
    {
        var nodeId = relayAddress is null ? "local" : "remote";
        string[] addresses = relayAddress is null ? ["127.0.0.1"] : ["127.0.0.1", relayAddress];
        using var node = authority.IssueNode(nodeId, addresses);
        await PrivateCertificateFile.WriteNewAsync(Path.Combine(directory, "node.pfx"), node.Export(X509ContentType.Pkcs12), CancellationToken.None).ConfigureAwait(false);
        var repository = await SqliteRegistryRepository.OpenAsync(state, "development", CancellationToken.None).ConfigureAwait(false);
        await using var repositoryLifetime = repository.ConfigureAwait(false);
        using var coordinator = new RegistryCoordinator(repository, new DestinationAvailabilityStore(TimeProvider.System), TimeProvider.System);
        await coordinator.InitializeAsync(CancellationToken.None).ConfigureAwait(false);
        var grant = new NodeGrant(nodeId, "development", node.GetCertHashString(HashAlgorithmName.SHA256), "svc", addresses, 1024, 65535,
            new DateTimeOffset(node.NotAfter.ToUniversalTime()), revoked: false);
        await coordinator.EnrollAsync(grant, "administrator", CancellationToken.None).ConfigureAwait(false);
    }

    private static string LocalRelayAddress()
    {
        foreach (var network in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            if (network.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                foreach (var unicast in network.GetIPProperties().UnicastAddresses)
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(unicast.Address) && !unicast.Address.Equals(IPAddress.Any)) return unicast.Address.ToString();
        throw new InvalidOperationException("Logical remote-node validation requires an active local IPv4 LAN interface.");
    }

    private async Task WaitUntilBoundAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (true)
        {
            _application?.ThrowIfExited(); _gateway.ThrowIfExited();
            try
            {
                await WaitForListenerAsync(Port, timeout.Token).ConfigureAwait(false);
                if (TlsPort > 0)
                {
                    await WaitForListenerAsync(TlsPort, timeout.Token).ConfigureAwait(false);
                    await WaitForListenerAsync(RegistrationPort, timeout.Token).ConfigureAwait(false);
                    if (ManagementPort > 0) await WaitForListenerAsync(ManagementPort, timeout.Token).ConfigureAwait(false);
                }
                if (TlsPort > 0 && _application is not null)
                {
                    using var probe = new DevelopmentSiteClient(RootCertificatePath, TlsPort, "probe.site.test");
                    using var healthy = await probe.Client.GetAsync(new Uri("/_drava/live", UriKind.Relative), timeout.Token).ConfigureAwait(false);
                    if (healthy.StatusCode != HttpStatusCode.OK) throw new HttpRequestException("Gateway presentation is not live.");
                }
                if (_application is null || OperatingSystem.IsWindows() || File.Exists(Ipc.UnixSocketPath)) return;
            }
            catch (Exception exception) when (exception is SocketException or HttpRequestException) { }
            await Task.Delay(25, timeout.Token).ConfigureAwait(false);
        }
    }

    private static async Task WaitForListenerAsync(int port, CancellationToken cancellationToken)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port), cancellationToken).ConfigureAwait(false);
    }

    public static int UnusedPort() => DevelopmentPortAllocator.GetPort();

    internal static string FindRoot()
    {
        var directory = new DirectoryInfo(Environment.GetEnvironmentVariable("MK8_DRAVA_DEVELOPMENT_CHECKOUT") ?? AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "mk8.drava.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Development checkout root not found.");
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _gateway.DisposeAsync().ConfigureAwait(false);
        if (_application is not null) await _application.DisposeAsync().ConfigureAwait(false);
        var evidence = Directory.CreateDirectory(Path.Combine(FindRoot(), "artifacts", "noconf-tests", Path.GetFileName(_directory))).FullName;
        await File.WriteAllTextAsync(Path.Combine(evidence, "gateway.log"), _gateway.CapturedLog).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(evidence, "application.log"), _application?.CapturedLog ?? "").ConfigureAwait(false);
        Directory.Delete(_directory, recursive: true);
    }
}

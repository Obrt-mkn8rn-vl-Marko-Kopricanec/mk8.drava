using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Mk8.Drava.Configuration;
using Mk8.Drava.Application.INF.Publication;
using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Application.DAL.Registry;
using Mk8.Drava.Application.DAL.Acme;

namespace Mk8.Drava.IntegrationTests;

internal sealed class TwoProcessProxy : IAsyncDisposable
{
    private readonly string _directory;
    private readonly DevelopmentProcess _application;
    private readonly DevelopmentProcess _gateway;
    public int Port { get; }
    public IpcEndpoint Ipc { get; }
    public HttpClient Client { get; }
    public int TlsPort { get; private set; }
    public int RegistrationPort { get; private set; }
    public string RootCertificatePath => Path.Combine(_directory, "site-root.der");
    public string NodeCertificatePath => Path.Combine(_directory, "node.pfx");

    private TwoProcessProxy(string directory, int port, IpcEndpoint ipc, DevelopmentProcess application, DevelopmentProcess gateway)
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
    public static async Task<TwoProcessProxy> StartAsync(int upstreamPort, string host = "app.test", bool enrolledSite = false)
    {
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
        var (applicationPath, gatewayPath) = await WriteBootstrapAsync(directory, state, gatewayState, ipc, port, tlsPort, registrationPort, upstreamPort, host, enrolledSite).ConfigureAwait(false);
        var root = FindRoot();
        var application = new DevelopmentProcess(Path.Combine(root, "mk8.drava.Application", "bin", "Release", "net10.0", "mk8.drava.Application.dll"), applicationPath);
        DevelopmentProcess? gateway = null;
        try
        {
            gateway = new DevelopmentProcess(Path.Combine(root, "mk8.drava.Gateway", "bin", "Release", "net10.0", "mk8.drava.Gateway.dll"), gatewayPath);
            var result = new TwoProcessProxy(directory, port, ipc, application, gateway) { TlsPort = tlsPort, RegistrationPort = registrationPort };
            try { await result.WaitUntilBoundAsync().ConfigureAwait(false); return result; }
            catch { result.Client.Dispose(); throw; }
        }
        catch
        {
            if (gateway is not null) await gateway.DisposeAsync().ConfigureAwait(false);
            await application.DisposeAsync().ConfigureAwait(false);
            Directory.Delete(directory, recursive: true);
            throw;
        }
    }

    private static async Task<(string Application, string Gateway)> WriteBootstrapAsync(string directory, string state, string gatewayState, IpcEndpoint ipc, int port, int tlsPort, int registrationPort, int upstreamPort, string host, bool enrolledSite)
    {
        ControllerBootstrap? controller = null;
        if (enrolledSite)
        {
            var authorityPath = Path.Combine(state, "site-ca.pfx");
            var fingerprint = await LocalSiteCertificateAuthority.InitializeAsync(authorityPath, "development", TimeProvider.System, CancellationToken.None).ConfigureAwait(false);
            using var authority = LocalSiteCertificateAuthority.Open(authorityPath, fingerprint, TimeProvider.System);
            using var rootCertificate = authority.PublicCertificate;
            await File.WriteAllBytesAsync(Path.Combine(directory, "site-root.der"), rootCertificate.RawData).ConfigureAwait(false);
            await EnrollDevelopmentNodeAsync(directory, state, authority).ConfigureAwait(false);
            controller = new ControllerBootstrap { Domain = "site.test", CertificateAuthorityPath = authorityPath, EnrollmentRootFingerprint = fingerprint, RegistrationPort = registrationPort };
        }
        var applicationBootstrap = new ApplicationBootstrap { SiteId = "development", NodeId = "local", StateDirectory = state, Listen = ipc, HttpPort = port, HttpsPort = tlsPort, Controller = controller };
        var gatewayBootstrap = new GatewayBootstrap { SiteId = "development", StateDirectory = gatewayState, Application = ipc, HttpPort = port, HttpsPort = tlsPort, RegistrationPort = registrationPort, EnrollmentRootFingerprint = controller?.EnrollmentRootFingerprint ?? "" };
        var sites = Directory.CreateDirectory(Path.Combine(state, "config", "sites")).FullName;
        await File.WriteAllTextAsync(Path.Combine(sites, "service.json"), JsonSerializer.Serialize(new
        {
            name = "test", host, listeners = new[] { new { name = "http", address = "127.0.0.1", port } },
            pathPrefix = "/", upstreams = new[] { new { name = "test", address = "127.0.0.1", port = upstreamPort } },
        })).ConfigureAwait(false);
        var applicationPath = Path.Combine(directory, "application.json");
        var gatewayPath = Path.Combine(directory, "gateway.json");
        await File.WriteAllTextAsync(applicationPath, BootstrapFile.Serialize(applicationBootstrap)).ConfigureAwait(false);
        await File.WriteAllTextAsync(gatewayPath, BootstrapFile.Serialize(gatewayBootstrap)).ConfigureAwait(false);
        return (applicationPath, gatewayPath);
    }

    private static async Task EnrollDevelopmentNodeAsync(string directory, string state, LocalSiteCertificateAuthority authority)
    {
        using var node = authority.IssueNode("local", ["127.0.0.1"]);
        await PrivateCertificateFile.WriteNewAsync(Path.Combine(directory, "node.pfx"), node.Export(X509ContentType.Pkcs12), CancellationToken.None).ConfigureAwait(false);
        var repository = await SqliteRegistryRepository.OpenAsync(state, "development", CancellationToken.None).ConfigureAwait(false);
        await using var repositoryLifetime = repository.ConfigureAwait(false);
        using var coordinator = new RegistryCoordinator(repository, new DestinationAvailabilityStore(TimeProvider.System), TimeProvider.System);
        await coordinator.InitializeAsync(CancellationToken.None).ConfigureAwait(false);
        var grant = new NodeGrant("local", "development", node.GetCertHashString(HashAlgorithmName.SHA256), "svc", ["127.0.0.1"], 1024, 65535,
            new DateTimeOffset(node.NotAfter.ToUniversalTime()), revoked: false);
        await coordinator.EnrollAsync(grant, "administrator", CancellationToken.None).ConfigureAwait(false);
    }

    private async Task WaitUntilBoundAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (true)
        {
            _application.ThrowIfExited(); _gateway.ThrowIfExited();
            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, Port), timeout.Token).ConfigureAwait(false);
                if (OperatingSystem.IsWindows() || File.Exists(Ipc.UnixSocketPath)) return;
            }
            catch (SocketException) { }
            await Task.Delay(25, timeout.Token).ConfigureAwait(false);
        }
    }

    public static int UnusedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
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
        await _application.DisposeAsync().ConfigureAwait(false);
        Directory.Delete(_directory, recursive: true);
    }
}

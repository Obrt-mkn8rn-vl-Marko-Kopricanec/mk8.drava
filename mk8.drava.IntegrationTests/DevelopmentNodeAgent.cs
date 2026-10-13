using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Mk8.Drava.Configuration;

namespace Mk8.Drava.IntegrationTests;

internal sealed class DevelopmentNodeAgent : IAsyncDisposable
{
    private readonly DevelopmentProcess _process;
    private readonly TwoProcessProxy _proxy;
    public NodeAgentDescriptor Descriptor { get; }
    public string StateDirectory { get; }

    private DevelopmentNodeAgent(DevelopmentProcess process, TwoProcessProxy proxy, NodeAgentDescriptor descriptor, string stateDirectory)
    { _process = process; _proxy = proxy; Descriptor = descriptor; StateDirectory = stateDirectory; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "Successful construction transfers the child process to this async-disposable fixture. Failed descriptor loading kills and joins the process in the catch; fixture disposal kills and joins it before preserving logs. The analyzer does not recognize this async ownership transfer.")]
    public static async Task<DevelopmentNodeAgent> StartAsync(TwoProcessProxy proxy, RelayLimits? relay = null, int mappingLeaseSeconds = 90, PrivateHttp2Settings? privateHttp2 = null,
        string stateDirectoryName = "agent", IReadOnlyDictionary<string, string>? environment = null)
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(proxy.NodeCertificatePath)!, stateDirectoryName)).FullName;
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var root = X509CertificateLoader.LoadCertificateFromFile(proxy.RootCertificatePath);
        var local = OperatingSystem.IsWindows() ? new IpcEndpoint { NamedPipeName = "drava_agent_" + Guid.NewGuid().ToString("N") }
            : new IpcEndpoint { UnixSocketPath = Path.Combine(directory, "agent.sock") };
        var bootstrap = new NodeAgentBootstrap
        {
            Site = new RegistrationSiteTrust { SiteId = "development", Domain = "site.test", RootFingerprint = root.GetCertHashString(HashAlgorithmName.SHA256),
                RootCertificatePath = proxy.RootCertificatePath, NodeCertificatePath = proxy.NodeCertificatePath, },
            NodeId = proxy.EnrolledNodeId, OwnerId = "development", ServicePrefix = "svc", StateDirectory = directory, LocalListen = local, LocalToken = proxy.NodeAgentToken,
            RelayAddress = proxy.NodeRelayAddress, RelayPort = 0, EndpointAddresses = proxy.NodeRelayAddress is "127.0.0.1" ? ["127.0.0.1"] : ["127.0.0.1", proxy.NodeRelayAddress],
            Relay = relay ?? new RelayLimits(), MappingLeaseSeconds = mappingLeaseSeconds,
            PrivateHttp2 = privateHttp2 ?? new PrivateHttp2Settings(),
        };
        var path = Path.Combine(directory, "agent.json");
        await PortableFixtureFile.WriteAsync(path, PortableConfiguration.Serialize(bootstrap)).ConfigureAwait(false);
        var profile = proxy.NodeCertificatePath + ".agent.json";
        var previous = File.Exists(profile) ? BootstrapFile.Parse<NodeAgentDescriptor>(await File.ReadAllBytesAsync(profile, CancellationToken.None).ConfigureAwait(false)) : null;
        var process = new DevelopmentProcess(DevelopmentBinaryPaths.ForProject("mk8.drava.Application"), path, "--node-agent", environment);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (true)
            {
                process.ThrowIfExited();
                if (File.Exists(profile))
                {
                    var descriptor = BootstrapFile.Parse<NodeAgentDescriptor>(await File.ReadAllBytesAsync(profile, timeout.Token).ConfigureAwait(false));
                    descriptor.Validate();
                    if (previous is null || !string.Equals(previous.AgentBootId, descriptor.AgentBootId, StringComparison.Ordinal))
                        return new DevelopmentNodeAgent(process, proxy, descriptor, directory);
                }
                await Task.Delay(25, timeout.Token).ConfigureAwait(false);
            }
        }
        catch { await process.DisposeAsync().ConfigureAwait(false); throw; }
    }

    public async ValueTask DisposeAsync()
    {
        await _process.DisposeAsync().ConfigureAwait(false);
        var certificateDirectory = Path.GetDirectoryName(_proxy.NodeCertificatePath) ?? throw new InvalidDataException("Development node certificate must have a parent directory.");
        var evidence = Directory.CreateDirectory(Path.Combine(TwoProcessProxy.FindRoot(), "artifacts", "node-relay-tests", Path.GetFileName(certificateDirectory))).FullName;
        await File.WriteAllTextAsync(Path.Combine(evidence, "node-agent-" + Descriptor.AgentBootId + ".log"), _process.CapturedLog).ConfigureAwait(false);
    }
}

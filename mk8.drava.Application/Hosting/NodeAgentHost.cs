using System.Net;
using System.Net.NetworkInformation;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Mk8.Drava.Application.BLL.NodeRelay;
using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Application.DAL.Acme;
using Mk8.Drava.Application.INF.NodeRelay;
using Mk8.Drava.Application.Transport;
using Mk8.Drava.Configuration;
using Mk8.Drava.Transport.Relay;

namespace Mk8.Drava.Application.Hosting;

internal static class NodeAgentHost
{
    public static async Task RunAsync(string path)
    {
        var bootstrap = await BootstrapFile.LoadAsync<NodeAgentBootstrap>(path, CancellationToken.None).ConfigureAwait(false);
        bootstrap.Validate();
        using var privateState = await PrivateApplicationState.OpenAsync(new ApplicationBootstrap { StateDirectory = bootstrap.StateDirectory, Listen = bootstrap.LocalListen }, CancellationToken.None).ConfigureAwait(false);
        using var node = X509CertificateLoader.LoadPkcs12(PrivateCertificateFile.Read(bootstrap.Site.NodeCertificatePath), password: null, X509KeyStorageFlags.EphemeralKeySet);
        using var root = X509CertificateLoader.LoadCertificateFromFile(bootstrap.Site.RootCertificatePath);
        var fingerprint = node.GetCertHashString(HashAlgorithmName.SHA256);
        if (!node.HasPrivateKey || !string.Equals(root.GetCertHashString(HashAlgorithmName.SHA256), bootstrap.Site.RootFingerprint, StringComparison.Ordinal) ||
            !NodeCertificateTrust.Validate(node, root, bootstrap.RelayAddress, fingerprint, TimeProvider.System)) throw new InvalidDataException("Node relay enrollment key, trust, role or listener name is invalid.");
        var localAddresses = ReadLocalAddresses();
        if (!localAddresses.Contains(bootstrap.RelayAddress)) throw new InvalidDataException("Relay listener does not belong to a local interface.");
        using var profileLock = OpenProfileLock(bootstrap.Site.NodeCertificatePath);
        var boot = Guid.NewGuid().ToString("N");
        var grant = new NodeGrant(bootstrap.NodeId, bootstrap.OwnerId, fingerprint, bootstrap.ServicePrefix, bootstrap.EndpointAddresses,
            bootstrap.MinimumPort, bootstrap.MaximumPort, new DateTimeOffset(node.NotAfter.ToUniversalTime()), revoked: false);
        var mappings = new NodeRelayMappings(bootstrap.Site.SiteId, boot, grant, localAddresses, TimeProvider.System);
        using var authorizer = new NodeRelayAuthorizer(bootstrap.Site.SiteId, root, mappings, TimeProvider.System);
        var policy = RelayPolicyMapping.ToPolicy(bootstrap.Relay);
        using var admission = new SemaphoreSlim(policy.MaximumConcurrentConnections, policy.MaximumConcurrentConnections);
        var descriptorState = new NodeAgentDescriptorState();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.Services.AddGrpc(options => { options.MaxReceiveMessageSize = 64 * 1024; options.MaxSendMessageSize = 64 * 1024; });
        builder.Services.AddSingleton(_ => new NodeRelayService(authorizer, admission, policy));
        builder.Services.AddSingleton(_ => new NodeAgentLocalService(mappings, descriptorState, bootstrap));
        builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(5));
        ConfigureListeners(builder, bootstrap, node, root);
        await RunHostAsync(builder, bootstrap, descriptorState, boot, fingerprint, mappings).ConfigureAwait(false);
    }

    private static void ConfigureListeners(WebApplicationBuilder builder, NodeAgentBootstrap bootstrap, X509Certificate2 node, X509Certificate2 root)
    {
        if (bootstrap.LocalListen.NamedPipeName.Length != 0)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Current-user pipes require Windows.");
            builder.WebHost.UseNamedPipes(options => options.CurrentUserOnly = true);
        }
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Limits.MaxRequestBodySize = null;
            options.Limits.Http2.InitialStreamWindowSize = 64 * 1024;
            options.Limits.Http2.InitialConnectionWindowSize = 256 * 1024;
            options.Limits.Http2.MaxStreamsPerConnection = 128;
            if (bootstrap.LocalListen.UnixSocketPath.Length != 0) options.ListenUnixSocket(bootstrap.LocalListen.UnixSocketPath, listener => listener.Protocols = HttpProtocols.Http2);
            else if (OperatingSystem.IsWindows()) options.ListenNamedPipe(bootstrap.LocalListen.NamedPipeName, listener => listener.Protocols = HttpProtocols.Http2);
            options.Listen(IPAddress.Parse(bootstrap.RelayAddress), bootstrap.RelayPort, listener =>
            {
                listener.Protocols = HttpProtocols.Http2;
                listener.UseHttps(https =>
                {
                    https.ServerCertificate = node; https.SslProtocols = SslProtocols.None;
                    https.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                    https.ClientCertificateValidation = (certificate, _, _) => ValidateController(certificate, root, bootstrap.Site.SiteId);
                });
            });
        });
    }

    private static bool ValidateController(X509Certificate2 certificate, X509Certificate2 root, string site)
    {
        try { ControllerCertificateRole.Validate(certificate, root, site, ControllerCertificateRole.Epoch(certificate, site), TimeProvider.System); return true; }
        catch (Exception exception) when (exception is UnauthorizedAccessException or CryptographicException or InvalidDataException or ArgumentException) { return false; }
    }

    private static async Task RunHostAsync(WebApplicationBuilder builder, NodeAgentBootstrap bootstrap, NodeAgentDescriptorState state, string boot, string fingerprint, NodeRelayMappings mappings)
    {
        var app = builder.Build();
        await using var appLifetime = app.ConfigureAwait(false);
        app.MapGrpcService<NodeRelayService>(); app.MapGrpcService<NodeAgentLocalService>();
        var profilePath = bootstrap.Site.NodeCertificatePath + ".agent.json";
        try
        {
            await app.StartAsync().ConfigureAwait(false);
            var bound = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>() ?? throw new InvalidDataException("Bound relay endpoint is absent.");
            var addresses = bound.Addresses.Where(static value => value.StartsWith("https://", StringComparison.Ordinal)).Select(static value => new Uri(value)).ToArray();
            if (addresses.Length != 1 || addresses[0].Port < bootstrap.MinimumPort || addresses[0].Port > bootstrap.MaximumPort) throw new InvalidDataException("Bound relay port is outside the enrollment scope.");
            var descriptor = new NodeAgentDescriptor { SiteId = bootstrap.Site.SiteId, NodeId = bootstrap.NodeId, AgentBootId = boot, RelayAddress = bootstrap.RelayAddress,
                RelayPort = addresses[0].Port, CertificateFingerprint = fingerprint, LocalEndpoint = bootstrap.LocalListen };
            state.Install(descriptor);
            await WriteProfileAsync(profilePath, descriptor).ConfigureAwait(false);
            await app.WaitForShutdownAsync().ConfigureAwait(false);
        }
        finally { mappings.Revoke(); File.Delete(profilePath); }
    }

    private static HashSet<string> ReadLocalAddresses()
    {
        var addresses = new HashSet<string>(StringComparer.Ordinal) { "127.0.0.1", "::1" };
        foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
            if (network.OperationalStatus == OperationalStatus.Up)
                foreach (var address in network.GetIPProperties().UnicastAddresses) addresses.Add(address.Address.ToString());
        return addresses;
    }

    private static FileStream OpenProfileLock(string certificatePath)
    {
        var path = certificatePath + ".agent.lock";
        if (new FileInfo(path).LinkTarget is not null) throw new InvalidDataException("Agent enrollment lock cannot be a symbolic link.");
        var options = new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return new FileStream(path, options);
    }

    private static async Task WriteProfileAsync(string path, NodeAgentDescriptor descriptor)
    {
        if (new FileInfo(path).LinkTarget is not null) throw new InvalidDataException("Agent descriptor cannot be a symbolic link.");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await PrivateCertificateFile.WriteNewAsync(temporary, Encoding.UTF8.GetBytes(BootstrapFile.Serialize(descriptor)), CancellationToken.None).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }
}

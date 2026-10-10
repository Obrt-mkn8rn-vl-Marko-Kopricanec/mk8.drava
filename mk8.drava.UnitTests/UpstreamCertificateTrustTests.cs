using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Upstreams;
using Mk8.Drava.Application.INF.Proxy.Connections;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class UpstreamCertificateTrustTests
{
    [Theory]
    [InlineData("matching")]
    [InlineData("wrong-name")]
    [InlineData("expired")]
    [InlineData("client-only")]
    [InlineData("wrong-root")]
    [InlineData("system-trust")]
    public async Task ActualTlsRequiresTheConfiguredAuthorityNameValidityAndServerUsageAsync(string profile)
    {
        using var directory = new RegistryStateDirectory();
        using var certificates = DevelopmentUpstreamTrustCertificates.Create(
            expired: string.Equals(profile, "expired", StringComparison.Ordinal), clientOnly: string.Equals(profile, "client-only", StringComparison.Ordinal));
        using var other = DevelopmentUpstreamTrustCertificates.Create();
        var root = string.Equals(profile, "wrong-root", StringComparison.Ordinal) ? other.Root : certificates.Root;
        var path = Path.Combine(directory.Path, "upstream-root.cer");
        await WriteRootAsync(path, root).ConfigureAwait(true);
        var configuredRoot = string.Equals(profile, "system-trust", StringComparison.Ordinal) ? null
            : new RuntimeTrustedRootCertificate(path, root.GetCertHashString(HashAlgorithmName.SHA256));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var endpoint = new UpstreamTransportEndpoint("peer", "https", "http1", "127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port, true,
            string.Equals(profile, "wrong-name", StringComparison.Ordinal) ? "other.drava.invalid" : "backend.drava.invalid") { TrustedRoot = configuredRoot };
        var serving = ServeTlsAsync(listener, certificates.Leaf, deadline.Token);
        try
        {
            if (string.Equals(profile, "matching", StringComparison.Ordinal))
            {
                using var connection = await new UpstreamConnectionFactory().ConnectAsync(endpoint, TimeSpan.FromSeconds(5), deadline.Token).ConfigureAwait(true);
                Assert.True(Assert.IsType<SslStream>(connection.Stream).IsAuthenticated);
                await connection.Stream.WriteAsync(new byte[] { 7 }, deadline.Token).ConfigureAwait(true);
                var response = new byte[1];
                await connection.Stream.ReadExactlyAsync(response, deadline.Token).ConfigureAwait(true);
                Assert.Equal((byte)9, response[0]);
                connection.Dispose();
            }
            else
            {
                await Assert.ThrowsAsync<UpstreamTlsException>(async () =>
                {
                    using var unexpected = await new UpstreamConnectionFactory().ConnectAsync(endpoint, TimeSpan.FromSeconds(5), deadline.Token).ConfigureAwait(true);
                }).ConfigureAwait(true);
            }
            var observed = await serving.WaitAsync(deadline.Token).ConfigureAwait(true);
            Assert.Equal(endpoint.EffectiveSniHost, observed.Sni);
            Assert.Equal(string.Equals(profile, "matching", StringComparison.Ordinal), observed.BusinessData);
        }
        finally
        {
            await deadline.CancelAsync().ConfigureAwait(true);
            try { await serving.ConfigureAwait(true); }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ATrustFileCannotSilentlyChangeItsPinnedCertificateOrUseALeafAsAnAuthorityAsync(bool leaf)
    {
        using var directory = new RegistryStateDirectory();
        using var certificates = DevelopmentUpstreamTrustCertificates.Create();
        var path = Path.Combine(directory.Path, "upstream-root.cer");
        var certificate = leaf ? certificates.Leaf : certificates.Root;
        await WriteRootAsync(path, certificate).ConfigureAwait(true);
        var endpoint = new UpstreamTransportEndpoint("peer", "https", "http1", "127.0.0.1", 443, true, "backend.drava.invalid")
        {
            TrustedRoot = new RuntimeTrustedRootCertificate(path, leaf ? certificate.GetCertHashString(HashAlgorithmName.SHA256) : new string('0', 64)),
        };
        Assert.Throws<InvalidDataException>(() => { using var unexpected = UpstreamCertificateTrust.Load(endpoint); });
    }

    internal static async Task WriteRootAsync(string path, X509Certificate2 root)
    {
        await File.WriteAllBytesAsync(path, root.RawData).ConfigureAwait(false);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static async Task<(string? Sni, bool BusinessData)> ServeTlsAsync(TcpListener listener, X509Certificate2 leaf, CancellationToken cancellationToken)
    {
        using var peer = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
        var tls = new SslStream(peer.GetStream(), leaveInnerStreamOpen: true);
        await using var tlsLifetime = tls.ConfigureAwait(false);
        string? sni = null;
        try
        {
            await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.None,
                ServerCertificateSelectionCallback = (_, name) => { sni = name; return leaf; },
            }, cancellationToken).ConfigureAwait(false);
            var marker = new byte[1];
            if (await tls.ReadAsync(marker, cancellationToken).ConfigureAwait(false) == 0) return (sni, false);
            if (marker[0] != 7) throw new InvalidDataException("Unexpected development request marker.");
            await tls.WriteAsync(new byte[] { 9 }, cancellationToken).ConfigureAwait(false);
            if (await tls.ReadAsync(marker, cancellationToken).ConfigureAwait(false) != 0)
                throw new InvalidDataException("The retired development TLS transport remained writable.");
            return (sni, true);
        }
        catch (Exception exception) when (exception is AuthenticationException or IOException)
        {
            return (sni, false);
        }
    }
}

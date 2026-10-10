using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Mk8.Drava.Application.INF.NoConf;
using Mk8.Drava.Configuration;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

public sealed class DnsPublisherNetworkTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ActualTlsProviderWritesRequireAnIndependentUdpDnsProofAsync(bool propagate)
    {
        using var material = await DevelopmentServingPlanFixture.CreateAsync().ConfigureAwait(true);
        using var certificate = material.Authority.IssueGateway("dns.invalid", ["127.0.0.1"]);
        using var root = material.Authority.PublicCertificate;
        var created = 0;
        var dns = new DevelopmentDnsServer(() => propagate && Volatile.Read(ref created) > 0 ? IPAddress.Loopback : null);
        await using var dnsLifetime = dns.ConfigureAwait(true);
        var api = await DevelopmentHttpUpstream.StartAsync(async context =>
        {
            Assert.Equal("api.dns.invalid", context.Request.Host.Host);
            Assert.Equal("Bearer " + new string('A', 40), context.Request.Headers.Authorization.ToString());
            if (HttpMethods.IsPost(context.Request.Method))
            {
                using var body = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted).ConfigureAwait(false);
                Assert.Equal("svc.site.test", body.RootElement.GetProperty("name").GetString());
                Assert.Equal("mk8.drava site=development", body.RootElement.GetProperty("comment").GetString());
                Assert.False(body.RootElement.GetProperty("proxied").GetBoolean());
                Assert.Equal(300, body.RootElement.GetProperty("ttl").GetInt32());
                Interlocked.Increment(ref created);
                await context.Response.WriteAsJsonAsync(new { success = true, errors = Array.Empty<object>(), result = new
                    { id = new string('b', 32), name = "svc.site.test", type = "A", content = "127.0.0.1", proxied = false, comment = "mk8.drava site=development" } }, context.RequestAborted).ConfigureAwait(false);
            }
            else if (context.Request.Path.Value?.EndsWith("/dns_records", StringComparison.Ordinal) == true)
            {
                Assert.Equal("svc.site.test", context.Request.Query["name.exact"].ToString());
                await context.Response.WriteAsJsonAsync(new { success = true, errors = Array.Empty<object>(), result = Array.Empty<object>(),
                    result_info = new { page = 1, total_count = 0, total_pages = 0 } }, context.RequestAborted).ConfigureAwait(false);
            }
            else await context.Response.WriteAsJsonAsync(new { success = true, errors = Array.Empty<object>(), result = new { name = "site.test" } }, context.RequestAborted).ConfigureAwait(false);
        }, certificate).ConfigureAwait(true);
        await using var apiLifetime = api.ConfigureAwait(true);
        var directory = Directory.CreateTempSubdirectory("mk8-drava-dns-network-").FullName;
        try
        {
            var settings = await CreateSettingsAsync(directory).ConfigureAwait(true);
            using var handler = ConnectProvider(root, api.Port);
            using var publisher = new CloudflareDnsPublisher(settings, "site.test", "development", ["127.0.0.1"], TimeProvider.System, handler);
            var verifier = new ServiceDnsVerifier(["127.0.0.1"], "127.0.0.1", dns.Port);
            Assert.False((await verifier.VerifyAsync("svc.site.test", CancellationToken.None).ConfigureAwait(true)).Verified);
            var composed = new PublishingServiceDnsVerifier(verifier, publisher);
            var proof = await composed.VerifyAsync("svc.site.test", CancellationToken.None).ConfigureAwait(true);
            Assert.Equal(propagate, proof.Verified);
            Assert.Equal(1, Volatile.Read(ref created));
            if (propagate) Assert.InRange(proof.Validity, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));
            Assert.Equal(propagate, (await composed.VerifyAsync("svc.site.test", CancellationToken.None).ConfigureAwait(true)).Verified);
            Assert.Equal(1, Volatile.Read(ref created));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static async Task<DnsPublicationSettings> CreateSettingsAsync(string directory)
    {
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var credential = Path.Combine(directory, "provider.token");
        await File.WriteAllTextAsync(credential, new string('A', 40)).ConfigureAwait(false);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(credential, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return new DnsPublicationSettings { Provider = "cloudflare", ApiBaseUrl = new Uri("https://api.dns.invalid/client/v4/"), ZoneId = new string('a', 32), ZoneName = "site.test", CredentialPath = credential };
    }

    private static SocketsHttpHandler ConnectProvider(X509Certificate2 root, int port) => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        SslOptions = new SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
            {
                if (certificate is null || (errors & (SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateNotAvailable)) != SslPolicyErrors.None) return false;
                using var leaf = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
                using var chain = new X509Chain();
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(root);
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                chain.ChainPolicy.DisableCertificateDownloads = true;
                chain.ChainPolicy.ApplicationPolicy.Add(new System.Security.Cryptography.Oid("1.3.6.1.5.5.7.3.1"));
                return chain.Build(leaf);
            },
        },
        ConnectCallback = async (context, cancellationToken) =>
        {
            Assert.Equal("api.dns.invalid", context.DnsEndPoint.Host);
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port), cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch { socket.Dispose(); throw; }
        },
    };
}

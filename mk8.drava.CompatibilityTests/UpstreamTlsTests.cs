using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Mk8.Drava.Application.INF.Configuration;
using Mk8.Drava.Application.DAL.Configuration.Loading;
using Mk8.Drava.Application.INF.Configuration.Loading;
using Mk8.Drava.Application.DAL.Configuration.Paths;
using Mk8.Drava.Application.INF.Proxy.Connections;
using Mk8.Drava.CompatibilityTests.LegacyIngress.Proxy.Connections;
using Mk8.Drava.Application.INF.Proxy.Health;
using Mk8.Drava.CompatibilityTests.LegacyApi.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests;
internal static class UpstreamTlsTests
{
    public static async Task ExistingHttpUpstreamConfigRemainsValid()
    {
        using var temp = TemporaryDirectory.Create();
        ConfigurationTests.WriteSite(temp.Path, "home.json", 18080, 15000);
        var result = await CreateLoader(temp.Path).LoadAsync(CancellationToken.None).ConfigureAwait(false);
        var upstream = ProxyConfigurationLoadResultAssertions.AssertLoadedSnapshot(result).Routes[0].Upstreams[0];
        AssertEx.Equal("http", upstream.Scheme);
        AssertEx.Equal("127.0.0.1:15000", upstream.Endpoint);
        AssertEx.True(upstream.Tls.ValidateCertificate);
    }

    public static async Task HttpsUpstreamConfigParsesAndValidates()
    {
        using var temp = TemporaryDirectory.Create();
        WriteHttpsUpstreamSite(temp.Path, "secure.json", 18080, 15443, "\"upstreamTls\": { \"sniHost\": \"app.internal\" }");
        var result = await CreateLoader(temp.Path).LoadAsync(CancellationToken.None).ConfigureAwait(false);
        var upstream = ProxyConfigurationLoadResultAssertions.AssertLoadedSnapshot(result).Routes[0].Upstreams[0];
        AssertEx.Equal("https", upstream.Scheme);
        AssertEx.Equal("app.internal", upstream.EffectiveSniHost);
        AssertEx.True(upstream.Tls.ValidateCertificate);
    }

    public static async Task UnsupportedUpstreamSchemeIsRejected()
    {
        using var temp = TemporaryDirectory.Create();
        WriteUpstreamSite(temp.Path, "bad-scheme.json", 18080, 15000, "ftp", "");
        var result = await CreateLoader(temp.Path).LoadAsync(CancellationToken.None).ConfigureAwait(false);
        ProxyConfigurationLoadResultAssertions.AssertFailed(result);
        AssertEx.True(result.Errors.Any(static error => error.Contains("Scheme", StringComparison.Ordinal)));
    }

    public static async Task AmbiguousUpstreamAddressIsRejected()
    {
        using var temp = TemporaryDirectory.Create();
        WriteUpstreamSite(temp.Path, "bad-address.json", 18080, 15000, "https", "");
        var path = Path.Combine(temp.Path, "config", "sites", "bad-address.json");
        var text = File.ReadAllText(path);
        const string addressProperty = "\"address\": \"127.0.0.1\"";
        var firstAddress = text.IndexOf(addressProperty, StringComparison.Ordinal);
        var upstreamAddress = text.IndexOf(addressProperty, firstAddress + addressProperty.Length, StringComparison.Ordinal);
        text = text.Remove(upstreamAddress, addressProperty.Length).Insert(upstreamAddress, "\"address\": \"https://127.0.0.1:15000/api\"");
        File.WriteAllText(path, text);
        var result = await CreateLoader(temp.Path).LoadAsync(CancellationToken.None).ConfigureAwait(false);
        ProxyConfigurationLoadResultAssertions.AssertFailed(result);
        AssertEx.True(result.Errors.Any(static error => error.Contains("Address", StringComparison.Ordinal)));
    }

    public static void PoolKeyDiffersForHttpAndHttps()
    {
        var http = Upstream(5001, "http", RuntimeUpstreamTlsOptions.Default);
        var https = Upstream(5001, "https", RuntimeUpstreamTlsOptions.Default);
        AssertEx.False(string.Equals(UpstreamConnectionPool.GetKey(UpstreamTransportEndpointMapper.FromUpstream(http)), UpstreamConnectionPool.GetKey(UpstreamTransportEndpointMapper.FromUpstream(https)), StringComparison.Ordinal));
    }

    public static void PoolKeyDiffersForDifferentSniAndValidation()
    {
        var firstSni = Upstream(5001, "https", new RuntimeUpstreamTlsOptions(true, "first.internal"));
        var secondSni = Upstream(5001, "https", new RuntimeUpstreamTlsOptions(true, "second.internal"));
        var unsafeValidation = Upstream(5001, "https", new RuntimeUpstreamTlsOptions(false, "first.internal"));
        AssertEx.False(string.Equals(UpstreamConnectionPool.GetKey(UpstreamTransportEndpointMapper.FromUpstream(firstSni)), UpstreamConnectionPool.GetKey(UpstreamTransportEndpointMapper.FromUpstream(secondSni)), StringComparison.Ordinal));
        AssertEx.False(string.Equals(UpstreamConnectionPool.GetKey(UpstreamTransportEndpointMapper.FromUpstream(firstSni)), UpstreamConnectionPool.GetKey(UpstreamTransportEndpointMapper.FromUpstream(unsafeValidation)), StringComparison.Ordinal));
    }

    public static async Task HttpsUpstreamUsesSslStreamPath()
    {
        var port = GetFreeTcpPort();
        using var certificate = CreateServerCertificate("upstream.test");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = RunSingleTlsResponseUpstreamAsync(port, certificate, "", timeout.Token);
        var upstream = Upstream(port, "https", new RuntimeUpstreamTlsOptions(false, "upstream.test"));
        using var transport = await new UpstreamConnectionFactory().ConnectAsync(UpstreamTransportEndpointMapper.FromUpstream(upstream), Timeouts().UpstreamConnectTimeout, timeout.Token).ConfigureAwait(false);
        AssertEx.True(transport.Stream is SslStream, transport.Stream.GetType().FullName);
        transport.Dispose();
        var observation = await serverTask.WaitAsync(timeout.Token).ConfigureAwait(false);
        AssertEx.True(observation.HandshakeSucceeded, observation.Error);
    }

    public static async Task HttpsUpstreamProxyForwardsThroughTls()
    {
        var proxyPort = GetFreeTcpPort();
        var upstreamPort = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var certificate = CreateServerCertificate("upstream.test");
        using var temp = TemporaryDirectory.Create();
        WriteHttpsUpstreamSite(temp.Path, "secure.json", proxyPort, upstreamPort, "\"upstreamTls\": { \"validateCertificate\": false, \"sniHost\": \"upstream.test\" }");
        var upstreamTask = RunSingleTlsResponseUpstreamAsync(upstreamPort, certificate, "HTTP/1.1 200 OK\r\nContent-Length: 6\r\n\r\nsecure", timeout.Token);
        using var host = BuildProxyHost(temp.Path);
        await host.StartAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var response = await SendSingleRequestAsync(proxyPort, "GET /secure HTTP/1.1\r\nHost: secure.test\r\nConnection: close\r\n\r\n", timeout.Token).ConfigureAwait(false);
            var observation = await upstreamTask.WaitAsync(timeout.Token).ConfigureAwait(false);
            AssertEx.True(response.Contains("200 OK", StringComparison.Ordinal), response);
            AssertEx.True(response.EndsWith("secure", StringComparison.Ordinal), response);
            AssertEx.True(observation.HandshakeSucceeded, observation.Error);
            AssertEx.True(observation.Request.StartsWith("GET /secure HTTP/1.1", StringComparison.Ordinal), observation.Request);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    public static async Task HttpsHealthChecksUseTlsSettings()
    {
        var upstreamPort = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var certificate = CreateServerCertificate("upstream.test");
        var upstream = Upstream(upstreamPort, "https", new RuntimeUpstreamTlsOptions(false, "upstream.test"));
        var route = Route([upstream]);
        var upstreamTask = RunSingleTlsResponseUpstreamAsync(upstreamPort, certificate, "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n", timeout.Token);
        var sample = await new UpstreamHealthCheckClient(new UpstreamConnectionFactory(), new ProxyMetrics()).CheckAsync(Target(route, upstream), timeout.Token).ConfigureAwait(false);
        var observation = await upstreamTask.WaitAsync(timeout.Token).ConfigureAwait(false);
        AssertEx.True(sample.Healthy, sample.Result);
        AssertEx.True(observation.HandshakeSucceeded, observation.Error);
        AssertEx.True(observation.Request.StartsWith("GET /health HTTP/1.1", StringComparison.Ordinal), observation.Request);
    }

    public static async Task CertificateValidationIsEnabledByDefault()
    {
        using var temp = TemporaryDirectory.Create();
        WriteHttpsUpstreamSite(temp.Path, "secure.json", 18080, 15443, "");
        var result = await CreateLoader(temp.Path).LoadAsync(CancellationToken.None).ConfigureAwait(false);
        var upstream = ProxyConfigurationLoadResultAssertions.AssertLoadedSnapshot(result).Routes[0].Upstreams[0];
        AssertEx.True(upstream.Tls.ValidateCertificate);
    }

    public static async Task ExplicitUnsafeValidationModeIsProjectedAsUnsafe()
    {
        using var temp = TemporaryDirectory.Create();
        WriteHttpsUpstreamSite(temp.Path, "secure.json", 18080, 15443, "\"upstreamTls\": { \"validateCertificate\": false, \"sniHost\": \"app.internal\" }");
        var result = await CreateLoader(temp.Path).LoadAsync(CancellationToken.None).ConfigureAwait(false);
        var snapshot = ProxyConfigurationLoadResultAssertions.AssertLoadedSnapshot(result);
        var projection = ProxyConfigurationProjectionMapper.ToProjection(snapshot, TestHttp3PlatformSupport.Project(snapshot));
        var upstream = projection.Routes[0].Upstreams[0];
        object tlsProjection = upstream.Tls;
        AssertEx.Equal("https", upstream.Scheme);
        AssertEx.False(upstream.Tls.ValidateCertificate);
        AssertEx.Equal("app.internal", upstream.EffectiveSniHost);
        AssertEx.True(tlsProjection is RuntimeUpstreamTlsProjection);
        AssertEx.False(tlsProjection is RuntimeUpstreamTlsOptions);
    }

    public static async Task TlsValidationFailureDoesNotFallBackToPlaintext()
    {
        var upstreamPort = GetFreeTcpPort();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var certificate = CreateServerCertificate("upstream.test");
        var upstream = Upstream(upstreamPort, "https", new RuntimeUpstreamTlsOptions(true, "upstream.test"));
        var upstreamTask = RunSingleTlsResponseUpstreamAsync(upstreamPort, certificate, "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok", timeout.Token);
        await AssertEx.ThrowsAsync<UpstreamTlsException>(async () =>
        {
            using var _ = await new UpstreamConnectionFactory().ConnectAsync(UpstreamTransportEndpointMapper.FromUpstream(upstream), Timeouts().UpstreamConnectTimeout, timeout.Token).ConfigureAwait(false);
        }).ConfigureAwait(false);
        var observation = await upstreamTask.WaitAsync(timeout.Token).ConfigureAwait(false);
        AssertEx.False(observation.Request.StartsWith("GET ", StringComparison.Ordinal), observation.Request);
    }

    public static async Task UpstreamSniOverrideValidationRejectsUrlPortAndWildcard()
    {
        using var urlTemp = TemporaryDirectory.Create();
        WriteHttpsUpstreamSite(urlTemp.Path, "url-sni.json", 18080, 15443, "\"upstreamTls\": { \"sniHost\": \"https://app.internal\" }");
        var urlResult = await CreateLoader(urlTemp.Path).LoadAsync(CancellationToken.None).ConfigureAwait(false);
        using var portTemp = TemporaryDirectory.Create();
        WriteHttpsUpstreamSite(portTemp.Path, "port-sni.json", 18081, 15444, "\"upstreamTls\": { \"sniHost\": \"app.internal:443\" }");
        var portResult = await CreateLoader(portTemp.Path).LoadAsync(CancellationToken.None).ConfigureAwait(false);
        using var wildcardTemp = TemporaryDirectory.Create();
        WriteHttpsUpstreamSite(wildcardTemp.Path, "wildcard-sni.json", 18082, 15445, "\"upstreamTls\": { \"sniHost\": \"*.internal\" }");
        var wildcardResult = await CreateLoader(wildcardTemp.Path).LoadAsync(CancellationToken.None).ConfigureAwait(false);
        ProxyConfigurationLoadResultAssertions.AssertFailed(urlResult);
        ProxyConfigurationLoadResultAssertions.AssertFailed(portResult);
        ProxyConfigurationLoadResultAssertions.AssertFailed(wildcardResult);
        AssertEx.True(urlResult.Errors.Any(static error => error.Contains("SniHost", StringComparison.Ordinal)));
        AssertEx.True(portResult.Errors.Any(static error => error.Contains("SniHost", StringComparison.Ordinal)));
        AssertEx.True(wildcardResult.Errors.Any(static error => error.Contains("SniHost", StringComparison.Ordinal)));
    }

    private static RuntimeRoute Route(IReadOnlyList<RuntimeUpstream> upstreams)
    {
        return new RuntimeRoute("test", "*", "/", RuntimeRouteAction.Proxy, "round-robin", new RuntimeHealthCheckOptions(true, "/health", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), 1, 1), upstreams, new RuntimeHttpsRedirectPolicy(false, 308, null), new RuntimeCanonicalHostPolicy(false, "", 308), RuntimeHeaderPolicy.Empty, new RuntimePathRewritePolicy("", "", ""), new RuntimeRedirectPolicy(308, "", "", true), new RuntimeStaticResponse(200, "text/plain; charset=utf-8", ""), new RuntimeMaintenancePolicy(false, null, "text/plain; charset=utf-8", "Service Unavailable"), RuntimeCachePolicy.Disabled, new RuntimeRouteResolvedOptions(100L * 1024 * 1024, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), true));
    }

    private static RuntimeUpstream Upstream(int port, string scheme, RuntimeUpstreamTlsOptions tls)
    {
        return new RuntimeUpstream("test", $"upstream-{port}", scheme, RuntimeUpstreamProtocol.Http1, "127.0.0.1", port, 1, tls);
    }

    private static UpstreamHealthCheckTarget Target(RuntimeRoute route, RuntimeUpstream upstream)
    {
        return new UpstreamHealthCheckTarget(route.Name, upstream.Name, upstream.Endpoint, upstream.Identity, UpstreamTransportEndpointMapper.FromUpstream(upstream), route.HealthCheck.Path, route.HealthCheck.Interval, route.HealthCheck.Timeout, route.HealthCheck.HealthyThreshold, route.HealthCheck.UnhealthyThreshold);
    }

    private static RuntimeTimeouts Timeouts()
    {
        return new RuntimeTimeouts(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    private static ProxyConfigurationLoader CreateLoader(string dataDirectory)
    {
        var provider = new MdravaDataDirectoryProvider(new MdravaDataDirectoryOptions { DataDirectory = dataDirectory });
        return new ProxyConfigurationLoader(provider, new ProxyDataDirectoryBootstrapper(provider), new SiteConfigurationParser(), new Mk8.Drava.Application.INF.Configuration.ProxyAdminUrlPolicy(), new ProxyEndpointAddressPolicy(), new ProxyRelativeStoragePathPolicy(), new Mk8.Drava.Application.INF.Configuration.ProxyUrlSyntaxPolicy(), new ProxyForwardedHeadersAddressPolicy(), NullLogger<ProxyConfigurationLoader>.Instance, TimeProvider.System);
    }

    private static IHost BuildProxyHost(string dataDirectory)
    {
        return Host.CreateDefaultBuilder().ConfigureAppConfiguration(builder =>
        {
            builder.Sources.Clear();
            builder.AddInMemoryCollection(new Dictionary<string, string?> { ["Mdrava:DataDirectory"] = dataDirectory });
        }).ConfigureLogging(logging => logging.ClearProviders()).ConfigureServices((context, services) =>
        {
            services.AddProxyDataPlane(context.Configuration);
        }).Build();
    }

    private static void WriteHttpsUpstreamSite(string dataDirectory, string fileName, int proxyPort, int upstreamPort, string upstreamTlsProperty)
    {
        WriteUpstreamSite(dataDirectory, fileName, proxyPort, upstreamPort, "https", upstreamTlsProperty);
    }

    private static void WriteUpstreamSite(string dataDirectory, string fileName, int proxyPort, int upstreamPort, string scheme, string upstreamTlsProperty)
    {
        var tlsSuffix = string.IsNullOrWhiteSpace(upstreamTlsProperty) ? "" : $"{Environment.NewLine}          ,{upstreamTlsProperty}";
        ConfigurationTests.WriteCustomSite(dataDirectory, fileName, $$"""
            {
              "name": "{{Path.GetFileNameWithoutExtension(fileName)}}",
              "listeners": [
                {
                  "name": "main",
                  "address": "127.0.0.1",
                  "port": {{proxyPort}}
                }
              ],
              "host": "*",
              "pathPrefix": "/",
              "upstreams": [
                {
                  "name": "local-test",
                  "scheme": "{{scheme}}",
                  "address": "127.0.0.1",
                  "port": {{upstreamPort}}{{tlsSuffix}}
                }
              ]
            }
            """);
    }

    private static async Task<TlsUpstreamObservation> RunSingleTlsResponseUpstreamAsync(int port, X509Certificate2 certificate, string response, CancellationToken cancellationToken)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        var request = "";
        var handshakeSucceeded = false;
        try
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            var stream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
            await using var streamDisposal = stream.ConfigureAwait(false);
            try
            {
                await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 }, cancellationToken).ConfigureAwait(false);
                handshakeSucceeded = true;
                request = await ReadHttpHeadAsync(stream, cancellationToken).ConfigureAwait(false);
                if (response.Length > 0)
                {
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(response), cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception exception)when (exception is AuthenticationException or IOException)
            {
                return new TlsUpstreamObservation(request, handshakeSucceeded, exception.GetType().Name);
            }

            return new TlsUpstreamObservation(request, handshakeSucceeded, null);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task<string> SendSingleRequestAsync(int proxyPort, string request, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxyPort, cancellationToken).ConfigureAwait(false);
        var stream = client.GetStream();
        await using var streamDisposal = stream.ConfigureAwait(false);
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request), cancellationToken).ConfigureAwait(false);
        return await ReadToEndAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> ReadToEndAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[1024];
        while (true)
        {
            var bytesRead = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                break;
            }

            buffer.Write(chunk, 0, bytesRead);
        }

        return Encoding.ASCII.GetString(buffer.ToArray());
    }

    private static async Task<string> ReadHttpHeadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var total = 0;
        while (total < buffer.Length)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(total, 1), cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                break;
            }

            total += bytesRead;
            if (total >= 4 && buffer[total - 4] == '\r' && buffer[total - 3] == '\n' && buffer[total - 2] == '\r' && buffer[total - 1] == '\n')
            {
                break;
            }
        }

        return Encoding.ASCII.GetString(buffer, 0, total);
    }

    private static X509Certificate2 CreateServerCertificate(string subjectName)
    {
        var pfxBytes = TestCertificates.CreateSelfSignedPfxBytes(subjectName);
        return X509CertificateLoader.LoadPkcs12(pfxBytes, ReadOnlySpan<char>.Empty, X509KeyStorageFlags.UserKeySet);
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private sealed record TlsUpstreamObservation(string Request, bool HandshakeSucceeded, string? Error);
    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mdrava-upstream-tls-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}

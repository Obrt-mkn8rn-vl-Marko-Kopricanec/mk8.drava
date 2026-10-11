using System.Diagnostics;
using System.Text;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Upstreams;
using Mk8.Drava.Application.INF.Proxy.Connections;
using Mk8.Drava.UnitTests;
using Xunit;
using Xunit.Abstractions;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
public sealed class VerifiedUpstreamTlsProcessTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output ?? throw new ArgumentNullException(nameof(output));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task GatewayAndApplicationPreserveSignedFieldsAndKnownLengthThroughVerifiedOriginTlsAsync(bool expectContinue) =>
        RunVerifiedUploadAsync(expectContinue, tcpNoDelay: null);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task GatewayAndApplicationPreserveSignedFieldsWithFrameworkTcpNoDelayAsync(bool expectContinue) =>
        RunVerifiedUploadAsync(expectContinue, tcpNoDelay: true);

    [Fact]
    public Task PlainFrontendToVerifiedTlsOriginIsolatesIncomingTlsAsync() =>
        RunVerifiedUploadAsync(expectContinue: false, tcpNoDelay: null, frontTls: false);

    [Fact]
    public Task TlsFrontendToPlainOriginIsolatesOutgoingTlsAsync() =>
        RunVerifiedUploadAsync(expectContinue: false, tcpNoDelay: true, originTls: false);

    private async Task RunVerifiedUploadAsync(bool expectContinue, bool? tcpNoDelay, bool frontTls = true, bool originTls = true)
    {
        const long bodyBytes = 30L * 1024 * 1024;
        const string rawTarget = "/container/mail/a%2Fb.bin?sig=a%2Bb%2Fc%3D&spr=https";
        using var directory = new RegistryStateDirectory();
        using var certificates = DevelopmentUpstreamTrustCertificates.Create();
        var tls = await PreparePolicyAsync(directory.Path, certificates).ConfigureAwait(true);
        var names = new ConcurrentQueue<string?>();
        var completed = 0;
        var headersValidated = 0;
        long receivedBytes = 0;
        var upstream = await DevelopmentHttpUpstream.StartAsync(async context =>
        {
            await ValidateUploadAsync(context, bodyBytes, rawTarget, count => Interlocked.Exchange(ref receivedBytes, count),
                () => Volatile.Write(ref headersValidated, 1), requireHttps: originTls).ConfigureAwait(false);
            Interlocked.Increment(ref completed);
        }, originTls ? certificates.Leaf : null, names.Enqueue).ConfigureAwait(true);
        await using var upstreamLifetime = upstream.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(upstream.Port, "svc.site.test", enrolledSite: true, verifiedUpstreamTls: originTls ? tls : null).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var client = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.TlsPort, "svc.site.test", tcpNoDelay: tcpNoDelay);
        using var content = new DevelopmentRepeatedBodyContent(bodyBytes);
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri(rawTarget, UriKind.Relative)) { Content = content };
        request.Headers.Host = "svc.site.test";
        request.Headers.ExpectContinue = expectContinue;
        request.Headers.Add("Authorization", "SharedKey synthetic:development-signature");
        request.Headers.Add("x-ms-version", "2026-06-06");
        request.Headers.IfNoneMatch.ParseAdd("*");
        var selectedClient = frontTls ? client.Client : proxy.Client;
        selectedClient.Timeout = TimeSpan.FromSeconds(10);
        var watch = Stopwatch.StartNew();
        try
        {
            using var response = await selectedClient.SendAsync(request).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal("\"development-etag\"", response.Headers.ETag?.Tag);
            Assert.Equal(1, Volatile.Read(ref completed));
            if (originTls) Assert.Contains("backend.drava.invalid", names, StringComparer.Ordinal);
            else Assert.Empty(names);
        }
        finally
        {
            // This is a bounded counter snapshot before fixture teardown, not a settled-origin completion claim.
            _output.WriteLine("expect_continue={0}; expected_bytes={1}; client_written_bytes={2}; origin_read_bytes={3}; origin_headers_validated={4}; origin_completed={5}; sni_observations={6}; send_elapsed_ms={7}; actual_tcp_no_delay={8}; front_tls={9}; origin_tls={10}",
                expectContinue, bodyBytes, content.SerializedBytes, Interlocked.Read(ref receivedBytes), Volatile.Read(ref headersValidated),
                Volatile.Read(ref completed), names.Count, watch.Elapsed.TotalMilliseconds, client.ObservedTcpNoDelay, frontTls, originTls);
        }
    }

    [Fact]
    public async Task NativeVerifiedTlsUploadControlPreservesTheOriginalTenSecondBudgetAsync()
    {
        // Diagnostic isolation: same finite origin/payload, native TLS path, no Gateway or RPC.
        const long bodyBytes = 30L * 1024 * 1024;
        const string rawTarget = "/container/mail/a%2Fb.bin?sig=a%2Bb%2Fc%3D&spr=https";
        using var directory = new RegistryStateDirectory();
        using var certificates = DevelopmentUpstreamTrustCertificates.Create();
        var tls = await PreparePolicyAsync(directory.Path, certificates).ConfigureAwait(true);
        var root = tls.TrustedRoot ?? throw new InvalidOperationException("The control requires verified root identity.");
        var names = new ConcurrentQueue<string?>();
        long received = 0;
        var completed = 0;
        var upstream = await DevelopmentHttpUpstream.StartAsync(async context =>
        {
            await ValidateUploadAsync(context, bodyBytes, rawTarget, count => Interlocked.Exchange(ref received, count), static () => { }).ConfigureAwait(false);
            Interlocked.Increment(ref completed);
        }, certificates.Leaf, names.Enqueue).ConfigureAwait(true);
        await using var lifetime = upstream.ConfigureAwait(true);
        var endpoint = new UpstreamTransportEndpoint("native-control", "https", "http1", "127.0.0.1", upstream.Port, ValidateCertificate: true, "backend.drava.invalid")
        {
            TrustedRoot = new RuntimeTrustedRootCertificate(root.CertificatePath, root.Sha256),
        };
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var content = new DevelopmentRepeatedBodyContent(bodyBytes);
        var watch = Stopwatch.StartNew();
        try
        {
            using var connection = await new UpstreamConnectionFactory().ConnectAsync(endpoint, TimeSpan.FromSeconds(3), deadline.Token).ConfigureAwait(true);
            var header = $"PUT {rawTarget} HTTP/1.1\r\nHost: svc.site.test\r\nContent-Length: 31457280\r\nAuthorization: SharedKey synthetic:development-signature\r\nx-ms-version: 2026-06-06\r\nIf-None-Match: *\r\nConnection: close\r\n\r\n";
            await connection.Stream.WriteAsync(Encoding.ASCII.GetBytes(header), deadline.Token).ConfigureAwait(true);
            await content.CopyToAsync(connection.Stream, deadline.Token).ConfigureAwait(true);
            var buffer = new byte[16384];
            var length = 0;
            while (length < buffer.Length)
            {
                var count = await connection.Stream.ReadAsync(buffer.AsMemory(length), deadline.Token).ConfigureAwait(true);
                if (count == 0) break;
                length += count;
                if (Encoding.ASCII.GetString(buffer, 0, length).Contains("\r\n\r\n", StringComparison.Ordinal)) break;
            }
            var response = Encoding.ASCII.GetString(buffer, 0, length);
            Assert.StartsWith("HTTP/1.1 201 ", response, StringComparison.Ordinal);
            Assert.Contains("ETag: \"development-etag\"", response, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(bodyBytes, Interlocked.Read(ref received));
            Assert.Equal(1, Volatile.Read(ref completed));
            Assert.Contains("backend.drava.invalid", names, StringComparer.Ordinal);
        }
        finally
        {
            _output.WriteLine("native_only_control=true; expected_bytes={0}; client_written_bytes={1}; origin_read_bytes={2}; origin_completed={3}; elapsed_ms={4}",
                bodyBytes, content.SerializedBytes, Interlocked.Read(ref received), Volatile.Read(ref completed), watch.Elapsed.TotalMilliseconds);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FrameworkTlsClientDirectOriginIsolatesGatewayInputAsync(bool gatewaySizedBuffer)
    {
        // Synthetic direct Kestrel control; the 64KiB row matches Gateway's existing buffer.
        const long bodyBytes = 30L * 1024 * 1024;
        const string rawTarget = "/container/mail/a%2Fb.bin?sig=a%2Bb%2Fc%3D&spr=https";
        using var directory = new RegistryStateDirectory();
        using var certificates = DevelopmentUpstreamTrustCertificates.Create(host: "svc.site.test");
        var root = Path.Combine(directory.Path, "direct-root.cer");
        await File.WriteAllBytesAsync(root, certificates.Root.RawData, CancellationToken.None).ConfigureAwait(true);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var names = new ConcurrentQueue<string?>();
        long received = 0;
        var completed = 0;
        var upstream = await DevelopmentHttpUpstream.StartAsync(async context =>
        {
            await ValidateUploadAsync(context, bodyBytes, rawTarget, count => Interlocked.Exchange(ref received, count), static () => { }).ConfigureAwait(false);
            Interlocked.Increment(ref completed);
        }, certificates.Leaf, names.Enqueue, maximumRequestBufferBytes: gatewaySizedBuffer ? 64 * 1024 : null).ConfigureAwait(true);
        await using var lifetime = upstream.ConfigureAwait(true);
        using var client = new DevelopmentSiteClient(root, upstream.Port, "svc.site.test", tcpNoDelay: true);
        using var content = new DevelopmentRepeatedBodyContent(bodyBytes);
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri(rawTarget, UriKind.Relative)) { Content = content };
        request.Headers.Host = "svc.site.test";
        request.Headers.ExpectContinue = false;
        request.Headers.Add("Authorization", "SharedKey synthetic:development-signature");
        request.Headers.Add("x-ms-version", "2026-06-06");
        request.Headers.IfNoneMatch.ParseAdd("*");
        var watch = Stopwatch.StartNew();
        try
        {
            using var response = await client.Client.SendAsync(request).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal("\"development-etag\"", response.Headers.ETag?.Tag);
            Assert.Equal(bodyBytes, Interlocked.Read(ref received));
            Assert.Equal(1, Volatile.Read(ref completed));
            Assert.Contains("svc.site.test", names, StringComparer.Ordinal);
        }
        finally
        {
            _output.WriteLine("framework_direct_tls=true; gateway_sized_buffer={0}; expected_bytes={1}; client_written_bytes={2}; origin_read_bytes={3}; origin_completed={4}; elapsed_ms={5}",
                gatewaySizedBuffer, bodyBytes, content.SerializedBytes, Interlocked.Read(ref received), Volatile.Read(ref completed), watch.Elapsed.TotalMilliseconds);
        }
    }

    [Fact]
    public async Task AHostnameMismatchNeverReachesTheBusinessHandlerAcrossBothProcessesAsync()
    {
        using var directory = new RegistryStateDirectory();
        using var certificates = DevelopmentUpstreamTrustCertificates.Create();
        var matching = await PreparePolicyAsync(directory.Path, certificates).ConfigureAwait(true);
        var mismatch = new UpstreamTlsOptions { ValidateCertificate = true, SniHost = "other.drava.invalid", TrustedRoot = matching.TrustedRoot };
        var requests = 0;
        var upstream = await DevelopmentHttpUpstream.StartAsync(context =>
        {
            Interlocked.Increment(ref requests);
            return context.Response.WriteAsync("unreachable", context.RequestAborted);
        }, certificates.Leaf).ConfigureAwait(true);
        await using var upstreamLifetime = upstream.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(upstream.Port, "svc.site.test", enrolledSite: true, verifiedUpstreamTls: mismatch).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var client = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.TlsPort, "svc.site.test");
        using var response = await client.Client.GetAsync(new Uri("/unreachable", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        await VerifyPrivatePathAsync(client.Client, new Uri("/admin/runtime.json", UriKind.Relative)).ConfigureAwait(true);
        await VerifyPrivatePathAsync(client.Client, new Uri("/admin/status", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(0, Volatile.Read(ref requests));
    }

    private static async Task VerifyPrivatePathAsync(HttpClient client, Uri path)
    {
        using var response = await client.GetAsync(path).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task ValidateUploadAsync(HttpContext context, long bodyBytes, string rawTarget, Action<long> observeBodyBytes, Action observeHeaders, bool requireHttps = true)
    {
        Assert.Equal(requireHttps, context.Request.IsHttps);
        Assert.Equal("PUT", context.Request.Method);
        Assert.Equal("svc.site.test", context.Request.Host.Value);
        Assert.Equal(rawTarget, context.Features.Get<IHttpRequestFeature>()?.RawTarget);
        Assert.Equal("SharedKey synthetic:development-signature", context.Request.Headers.Authorization.ToString());
        Assert.Equal("2026-06-06", context.Request.Headers["x-ms-version"].ToString());
        Assert.Equal("*", context.Request.Headers.IfNoneMatch.ToString());
        Assert.Equal(bodyBytes, context.Request.ContentLength);
        var admission = context.Features.Get<IHttpMaxRequestBodySizeFeature>() ?? throw new InvalidOperationException("The development origin has no body admission feature.");
        admission.MaxRequestBodySize = bodyBytes;
        observeHeaders();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[32 * 1024];
        long received = 0;
        int count;
        while ((count = await context.Request.Body.ReadAsync(buffer, context.RequestAborted).ConfigureAwait(false)) != 0)
        {
            received += count;
            observeBodyBytes(received);
            hash.AppendData(buffer.AsSpan(0, count));
        }
        Assert.Equal(bodyBytes, received);
        Assert.Equal(DevelopmentRepeatedBodyContent.ExpectedHash(bodyBytes), Convert.ToHexString(hash.GetHashAndReset()));
        context.Response.StatusCode = StatusCodes.Status201Created;
        context.Response.Headers.ETag = "\"development-etag\"";
    }

    private static async Task<UpstreamTlsOptions> PreparePolicyAsync(string directory, DevelopmentUpstreamTrustCertificates certificates)
    {
        var path = Path.Combine(directory, "upstream-root.cer");
        await File.WriteAllBytesAsync(path, certificates.Root.RawData).ConfigureAwait(false);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return new UpstreamTlsOptions
        {
            ValidateCertificate = true, SniHost = "backend.drava.invalid",
            TrustedRoot = new TrustedRootCertificateOptions { CertificatePath = path, Sha256 = certificates.Root.GetCertHashString(HashAlgorithmName.SHA256) },
        };
    }
}

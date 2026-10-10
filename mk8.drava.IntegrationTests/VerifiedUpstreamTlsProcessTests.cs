using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.UnitTests;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
public sealed class VerifiedUpstreamTlsProcessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GatewayAndApplicationPreserveSignedFieldsAndKnownLengthThroughVerifiedOriginTlsAsync(bool expectContinue)
    {
        const long bodyBytes = 30L * 1024 * 1024;
        const string rawTarget = "/container/mail/a%2Fb.bin?sig=a%2Bb%2Fc%3D&spr=https";
        using var directory = new RegistryStateDirectory();
        using var certificates = DevelopmentUpstreamTrustCertificates.Create();
        var tls = await PreparePolicyAsync(directory.Path, certificates).ConfigureAwait(true);
        var names = new ConcurrentQueue<string?>();
        var completed = 0;
        var upstream = await DevelopmentHttpUpstream.StartAsync(async context =>
        {
            await ValidateUploadAsync(context, bodyBytes, rawTarget).ConfigureAwait(false);
            Interlocked.Increment(ref completed);
        }, certificates.Leaf, names.Enqueue).ConfigureAwait(true);
        await using var upstreamLifetime = upstream.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(upstream.Port, "svc.site.test", enrolledSite: true, verifiedUpstreamTls: tls).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var client = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.TlsPort, "svc.site.test");
        using var content = new DevelopmentRepeatedBodyContent(bodyBytes);
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri(rawTarget, UriKind.Relative)) { Content = content };
        request.Headers.Host = "svc.site.test";
        request.Headers.ExpectContinue = expectContinue;
        request.Headers.Add("Authorization", "SharedKey synthetic:development-signature");
        request.Headers.Add("x-ms-version", "2026-06-06");
        request.Headers.IfNoneMatch.ParseAdd("*");
        using var response = await client.Client.SendAsync(request).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("\"development-etag\"", response.Headers.ETag?.Tag);
        Assert.Equal(1, Volatile.Read(ref completed));
        Assert.Contains("backend.drava.invalid", names, StringComparer.Ordinal);
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

    private static async Task ValidateUploadAsync(HttpContext context, long bodyBytes, string rawTarget)
    {
        Assert.True(context.Request.IsHttps);
        Assert.Equal("PUT", context.Request.Method);
        Assert.Equal("svc.site.test", context.Request.Host.Value);
        Assert.Equal(rawTarget, context.Features.Get<IHttpRequestFeature>()?.RawTarget);
        Assert.Equal("SharedKey synthetic:development-signature", context.Request.Headers.Authorization.ToString());
        Assert.Equal("2026-06-06", context.Request.Headers["x-ms-version"].ToString());
        Assert.Equal("*", context.Request.Headers.IfNoneMatch.ToString());
        Assert.Equal(bodyBytes, context.Request.ContentLength);
        var admission = context.Features.Get<IHttpMaxRequestBodySizeFeature>() ?? throw new InvalidOperationException("The development origin has no body admission feature.");
        admission.MaxRequestBodySize = bodyBytes;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[32 * 1024];
        long received = 0;
        int count;
        while ((count = await context.Request.Body.ReadAsync(buffer, context.RequestAborted).ConfigureAwait(false)) != 0)
        {
            received += count;
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

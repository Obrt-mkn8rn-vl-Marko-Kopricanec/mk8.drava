using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Mk8.Drava.Configuration;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
public sealed class ScopedBlobIngressTests
{
    private const long OneShotThreshold = 256L * 1024 * 1024;
    private const long StructuredOverhead = 21L + 18L * ushort.MaxValue;
    private const long TransportCeiling = OneShotThreshold + StructuredOverhead;
    private const string BlobHost = "blob.site.test";

    [Theory]
    [InlineData(OneShotThreshold - 1)]
    [InlineData(TransportCeiling)]
    public async Task ScopedLargeKnownLengthTransportPreservesBytesAndRawSignatureInputsAsync(long length)
    {
        const string target = "/account/a%2Fb?sig=abc%2F%2B%3D&comp=block";
        var upstream = await DevelopmentHttpUpstream.StartAsync(async context =>
        {
            var limit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            Assert.NotNull(limit);
            limit.MaxRequestBodySize = TransportCeiling;
            Assert.Equal(BlobHost, context.Request.Host.Value);
            Assert.Equal(target, context.Features.Get<IHttpRequestFeature>()?.RawTarget);
            Assert.Equal("Bearer fixture-nonsecret", context.Request.Headers.Authorization.ToString());
            Assert.Equal("2026-06-06", context.Request.Headers["x-ms-version"].ToString());
            Assert.Equal(length, context.Request.ContentLength);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[32 * 1024];
            long bytes = 0;
            int count;
            while ((count = await context.Request.Body.ReadAsync(buffer, context.RequestAborted).ConfigureAwait(false)) != 0)
            {
                hash.AppendData(buffer.AsSpan(0, count));
                bytes += count;
            }
            Assert.Equal(length, bytes);
            await context.Response.WriteAsync(Convert.ToHexString(hash.GetHashAndReset()), context.RequestAborted).ConfigureAwait(false);
        }).ConfigureAwait(true);
        await using var upstreamLifetime = upstream.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(upstream.Port, host: BlobHost,
            requestBodyLimits: [new GatewayRequestBodyLimit { Host = BlobHost, MaxRequestBodyBytes = TransportCeiling }],
            routeBodyLimit: TransportCeiling).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        proxy.Client.Timeout = TimeSpan.FromMinutes(2);
        proxy.Client.DefaultRequestHeaders.Host = BlobHost;
        using var content = new DevelopmentRepeatedBodyContent(length);
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri(target, UriKind.Relative)) { Content = content };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "fixture-nonsecret");
        request.Headers.Add("x-ms-version", "2026-06-06");
        request.Headers.ExpectContinue = true;
        using var response = await proxy.Client.SendAsync(request).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(content.SerializationStarted);
        Assert.Equal(DevelopmentRepeatedBodyContent.ExpectedHash(length), await response.Content.ReadAsStringAsync().ConfigureAwait(true));
    }

    [Theory]
    [InlineData(BlobHost, TransportCeiling + 1)]
    [InlineData("email.site.test", 100L * 1024 * 1024 + 1)]
    public async Task OversizeHostAdmissionRejectsBeforeUploadOrUpstreamContactAsync(string host, long length)
    {
        var requests = 0;
        var upstream = await DevelopmentHttpUpstream.StartAsync(context =>
        {
            Interlocked.Increment(ref requests);
            return context.Response.WriteAsync("unexpected");
        }).ConfigureAwait(true);
        await using var upstreamLifetime = upstream.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(upstream.Port, host: BlobHost,
            requestBodyLimits: [new GatewayRequestBodyLimit { Host = BlobHost, MaxRequestBodyBytes = TransportCeiling }],
            routeBodyLimit: TransportCeiling).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var content = new DevelopmentRepeatedBodyContent(length);
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri("/account/object", UriKind.Relative)) { Content = content };
        request.Headers.Host = host;
        request.Headers.ExpectContinue = true;
        using var response = await proxy.Client.SendAsync(request).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.False(content.SerializationStarted);
        Assert.Equal(0, Volatile.Read(ref requests));
    }
}

using System.Net;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

public sealed class RepresentationContentLengthProcessTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task HeadRepresentationLengthSurvivesMissAndHitAcrossBothProtocolsAsync(bool upstreamHttp2, bool publicHttp2, bool cacheEnabled)
    {
        using var certificate = DevelopmentUpstreamCertificate.Create();
        var calls = 0;
        var upstream = await DevelopmentHttpUpstream.StartAsync(context =>
        {
            Interlocked.Increment(ref calls);
            context.Response.ContentLength = 42;
            context.Response.Headers.CacheControl = "max-age=60";
            return Task.CompletedTask;
        }, upstreamHttp2 ? certificate : null, http2: upstreamHttp2).ConfigureAwait(true);
        await using var upstreamLifetime = upstream.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(upstream.Port, "svc.site.test", enrolledSite: true, upstreamHttp2: upstreamHttp2, manualCache: cacheEnabled).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var client = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.TlsPort, "svc.site.test");
        client.Client.DefaultRequestVersion = publicHttp2 ? HttpVersion.Version20 : HttpVersion.Version11;
        for (var call = 0; call < 2; call++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, new Uri("/representation", UriKind.Relative))
                { Version = client.Client.DefaultRequestVersion, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
            using var response = await client.Client.SendAsync(request).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(request.Version, response.Version);
            Assert.Equal(42L, response.Content.Headers.ContentLength);
            Assert.Empty(await response.Content.ReadAsByteArrayAsync().ConfigureAwait(true));
        }
        Assert.Equal(cacheEnabled ? 1 : 2, Volatile.Read(ref calls));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task NotModifiedRetainsItsRepresentationSizeWithoutTransferringContentAsync(bool upstreamHttp2, bool publicHttp2)
    {
        using var certificate = DevelopmentUpstreamCertificate.Create();
        var upstream = await DevelopmentHttpUpstream.StartAsync(context =>
        {
            context.Response.StatusCode = StatusCodes.Status304NotModified;
            context.Response.ContentLength = 42;
            return Task.CompletedTask;
        }, upstreamHttp2 ? certificate : null, http2: upstreamHttp2).ConfigureAwait(true);
        await using var upstreamLifetime = upstream.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(upstream.Port, "svc.site.test", enrolledSite: true, upstreamHttp2: upstreamHttp2).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var client = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.TlsPort, "svc.site.test");
        client.Client.DefaultRequestVersion = publicHttp2 ? HttpVersion.Version20 : HttpVersion.Version11;
        using var response = await client.Client.GetAsync(new Uri("/not-modified", UriKind.Relative)).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
        Assert.Equal(client.Client.DefaultRequestVersion, response.Version);
        Assert.Equal(42L, response.Content.Headers.ContentLength);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync().ConfigureAwait(true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownHeadRepresentationLengthStaysAbsentOnTheActualCacheHitAsync(bool publicHttp2)
    {
        using var certificate = DevelopmentUpstreamCertificate.Create();
        using var upstream = new DevelopmentHttp2Peer(certificate);
        var proxy = await TwoProcessProxy.StartAsync(upstream.Port, "svc.site.test", enrolledSite: true, upstreamHttp2: true, manualCache: true).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var client = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.TlsPort, "svc.site.test");
        client.Client.DefaultRequestVersion = publicHttp2 ? HttpVersion.Version20 : HttpVersion.Version11;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var peer = upstream.RespondHeadAsync(200, null, deadline.Token);
        try
        {
            await VerifyAbsentHeadLengthAsync(client.Client, deadline.Token).ConfigureAwait(true);
            await peer.ConfigureAwait(true);
            // No peer accepts another connection: a body-only miss would time out.
            await VerifyAbsentHeadLengthAsync(client.Client, deadline.Token).ConfigureAwait(true);
        }
        finally
        {
            await deadline.CancelAsync().ConfigureAwait(true);
            try { await peer.ConfigureAwait(true); }
            catch (Exception exception) when (exception is OperationCanceledException or IOException) { }
        }
    }

    private static async Task VerifyAbsentHeadLengthAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, new Uri("/unknown-head", UriKind.Relative))
            { Version = client.DefaultRequestVersion, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Content.Headers.Contains("Content-Length"));
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(true));
    }
}

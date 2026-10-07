using System.Net;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

public sealed class FramedUpstreamBodyProcessTests
{
    [Theory]
    [InlineData(3L, 2)]
    [InlineData(1L, 4)]
    public async Task StreamingLengthFailuresAbortThePresentedResponseAsync(long declaredLength, int actualBytes)
    {
        using var certificate = DevelopmentUpstreamCertificate.Create();
        using var upstream = new DevelopmentHttp2Peer(certificate);
        var proxy = await TwoProcessProxy.StartAsync(upstream.Port, "svc.site.test", enrolledSite: true, upstreamHttp2: true).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var client = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.TlsPort, "svc.site.test");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var peer = upstream.RespondAsync(declaredLength, new byte[actualBytes], false, true, deadline.Token);
        try
        {
            using var response = await client.Client.GetAsync(new Uri("/stream-integrity", UriKind.Relative),
                HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var exception = await Record.ExceptionAsync(async () =>
                await response.Content.ReadAsByteArrayAsync(deadline.Token).ConfigureAwait(true)).ConfigureAwait(true);
            Assert.True(exception is HttpRequestException or IOException, exception?.ToString() ?? "Malformed upstream response completed successfully.");
            await peer.ConfigureAwait(true);
        }
        finally
        {
            await deadline.CancelAsync().ConfigureAwait(true);
            try { await peer.ConfigureAwait(true); }
            catch (Exception exception) when (exception is OperationCanceledException or IOException) { }
        }
    }

    [Theory]
    [InlineData(3L, 0, true)]
    [InlineData(3L, 2, false)]
    [InlineData(1L, 16384, false)]
    [InlineData(0L, 1, false)]
    public async Task MalformedCacheCandidatesFailBeforePresentationAndAreNeverStoredAsync(long declaredLength, int actualBytes, bool endedWithHead)
    {
        using var certificate = DevelopmentUpstreamCertificate.Create();
        using var upstream = new DevelopmentHttp2Peer(certificate);
        var proxy = await TwoProcessProxy.StartAsync(upstream.Port, "svc.site.test", enrolledSite: true, upstreamHttp2: true, manualCache: true).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var client = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.TlsPort, "svc.site.test");
        for (var call = 0; call < 2; call++)
            await VerifyMalformedCacheAttemptAsync(upstream, client, declaredLength, actualBytes, endedWithHead).ConfigureAwait(true);
    }

    private static async Task VerifyMalformedCacheAttemptAsync(DevelopmentHttp2Peer upstream, DevelopmentSiteClient client,
        long declaredLength, int actualBytes, bool endedWithHead)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var peer = upstream.RespondAsync(declaredLength, new byte[actualBytes], endedWithHead, false, deadline.Token);
        try
        {
            using var response = await client.Client.GetAsync(new Uri("/cache-integrity", UriKind.Relative), deadline.Token).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
            await peer.ConfigureAwait(true);
        }
        finally
        {
            await deadline.CancelAsync().ConfigureAwait(true);
            try { await peer.ConfigureAwait(true); }
            catch (Exception exception) when (exception is OperationCanceledException or IOException) { }
        }
    }

    [Fact]
    public async Task AnOversizedValidResponseStreamsWithoutEnteringTheCacheAsync()
    {
        using var certificate = DevelopmentUpstreamCertificate.Create();
        using var upstream = new DevelopmentHttp2Peer(certificate);
        var proxy = await TwoProcessProxy.StartAsync(upstream.Port, "svc.site.test", enrolledSite: true, upstreamHttp2: true, manualCache: true).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var client = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.TlsPort, "svc.site.test");
        for (var call = 0; call < 2; call++)
            await VerifyOversizedAttemptAsync(upstream, client, call).ConfigureAwait(true);
    }

    private static async Task VerifyOversizedAttemptAsync(DevelopmentHttp2Peer upstream, DevelopmentSiteClient client, int call)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var body = new byte[8192]; Array.Fill(body, (byte)call);
        var peer = upstream.RespondAsync(body.Length, body, false, false, deadline.Token);
        try
        {
            using var response = await client.Client.GetAsync(new Uri("/oversized", UriKind.Relative), deadline.Token).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(body, await response.Content.ReadAsByteArrayAsync(deadline.Token).ConfigureAwait(true));
            await peer.ConfigureAwait(true);
        }
        finally
        {
            await deadline.CancelAsync().ConfigureAwait(true);
            try { await peer.ConfigureAwait(true); }
            catch (Exception exception) when (exception is OperationCanceledException or IOException) { }
        }
    }
}

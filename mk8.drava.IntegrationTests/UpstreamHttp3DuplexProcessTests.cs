using System.Net;
using System.Net.Quic;
using System.Runtime.Versioning;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("osx")]
public sealed class UpstreamHttp3DuplexProcessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LargeDuplexEchoTraversesBothProcessesAndTheActualQuicUpstreamAsync(bool clientHttp2)
    {
        using var setup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var listener = await DevelopmentPendingQuicPeer.CreateAsync(setup.Token).ConfigureAwait(true);
        await using var listenerLifetime = listener.ConfigureAwait(true);
        listener.ReleaseHandshake();
        var proxy = await TwoProcessProxy.StartAsync(listener.Port, "svc.site.test", enrolledSite: clientHttp2, upstreamHttp3: true).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        var peer = new DevelopmentHttp3DuplexPeer(listener);
        using var secure = clientHttp2 ? new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.TlsPort, "svc.site.test") : null;
        var client = secure?.Client ?? proxy.Client;
        client.DefaultRequestHeaders.Host = "svc.site.test";
        var body = new byte[2 * 1024 * 1024];
        for (var index = 0; index < body.Length; index++) body[index] = (byte)(index % 251);
        using var deadline = new CancellationTokenSource(client.Timeout);
        var responseTask = peer.RespondAsync(false, body.Length, deadline.Token);
        try
        {
            var content = new DevelopmentDuplexContent(body, client.Timeout);
            using var request = new HttpRequestMessage(HttpMethod.Post, "/echo") { Content = content,
                Version = clientHttp2 ? HttpVersion.Version20 : HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
            await using var contentLifetime = content.ConfigureAwait(true);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(clientHttp2 ? HttpVersion.Version20 : HttpVersion.Version11, response.Version);
            Assert.Equal(body, await response.Content.ReadAsByteArrayAsync(deadline.Token).ConfigureAwait(true));
            peer.FinishResponse();
            await responseTask.ConfigureAwait(true);
        }
        finally
        {
            peer.FinishResponse();
            await deadline.CancelAsync().ConfigureAwait(true);
            try { await responseTask.ConfigureAwait(true); }
            catch (Exception exception) when (exception is OperationCanceledException or IOException or QuicException) { }
        }
    }
}

using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Http;
using Xunit;
using Xunit.Abstractions;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
public sealed class UpstreamHttp3KestrelDownloadProcessTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LargeSingleWriteResponseTraversesBothProcessesFromAnActualHttp3ServerAsync(bool clientHttp2)
    {
        var body = new byte[2 * 1024 * 1024];
        for (var index = 0; index < body.Length; index++) body[index] = (byte)(index % 251);
        using var certificate = DevelopmentUpstreamCertificate.Create();
        var protocol = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var originWriteState = 0;
        var upstream = await DevelopmentHttpUpstream.StartAsync(async context =>
        {
            protocol.TrySetResult(context.Request.Protocol);
            context.Response.ContentLength = body.Length;
            Volatile.Write(ref originWriteState, 1);
            await context.Response.Body.WriteAsync(body, context.RequestAborted).ConfigureAwait(false);
            Volatile.Write(ref originWriteState, 2);
        }, certificate, configureListener: static listener => listener.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http3).ConfigureAwait(true);
        await using var upstreamLifetime = upstream.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(upstream.Port, "svc.site.test", enrolledSite: clientHttp2, upstreamHttp3: true, observeForwarding: true).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var secure = clientHttp2 ? new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.TlsPort, "svc.site.test") : null;
        var client = secure?.Client ?? proxy.Client;
        client.DefaultRequestHeaders.Host = "svc.site.test";
        using var deadline = new CancellationTokenSource(client.Timeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/download")
        {
            Version = clientHttp2 ? HttpVersion.Version20 : HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
        var elapsed = Stopwatch.StartNew();
        var phase = "public-response-headers";
        output.WriteLine("http3_download_fixture: {0}", proxy.EvidenceId);
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(true);
            phase = "response-head-assertions";
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(clientHttp2 ? HttpVersion.Version20 : HttpVersion.Version11, response.Version);
            phase = "origin-protocol-assertion";
            Assert.Equal("HTTP/3", await protocol.Task.ConfigureAwait(true));
            phase = "response-body-assertion";
            Assert.Equal(body, await response.Content.ReadAsByteArrayAsync(deadline.Token).ConfigureAwait(true));
            phase = "complete";
        }
        finally
        {
            output.WriteLine("http3_download_phase: fixture={0}; public_http2={1}; phase={2}; elapsed_ms={3}; deadline_cancelled={4}; origin_protocol_task={5}; origin_write_state={6}",
                proxy.EvidenceId, clientHttp2, phase, elapsed.Elapsed.TotalMilliseconds, deadline.IsCancellationRequested, protocol.Task.Status, Volatile.Read(ref originWriteState));
        }
    }
}

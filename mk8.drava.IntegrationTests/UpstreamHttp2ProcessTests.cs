using System.Net;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
public sealed class UpstreamHttp2ProcessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LargeDuplexEchoTraversesTheGatewayAndAnActualTlsHttp2UpstreamAsync(bool clientHttp2)
    {
        using var certificate = DevelopmentUpstreamCertificate.Create();
        var protocol = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var upstream = await DevelopmentHttpUpstream.StartAsync(async context =>
        {
            protocol.TrySetResult(context.Request.Protocol);
            await context.Request.Body.CopyToAsync(context.Response.Body, context.RequestAborted).ConfigureAwait(false);
        }, certificate, http2: true).ConfigureAwait(true);
        await using var upstreamLifetime = upstream.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(upstream.Port, "svc.site.test", enrolledSite: clientHttp2, upstreamHttp2: true).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var secure = clientHttp2 ? new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.TlsPort, "svc.site.test") : null;
        var client = secure?.Client ?? proxy.Client;
        client.DefaultRequestHeaders.Host = "svc.site.test";
        var body = new byte[2 * 1024 * 1024];
        for (var index = 0; index < body.Length; index++) body[index] = (byte)(index % 251);
        using var deadline = new CancellationTokenSource(client.Timeout);
        var content = new DevelopmentDuplexContent(body, client.Timeout);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/echo") { Content = content,
            Version = clientHttp2 ? HttpVersion.Version20 : HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
        await using var contentLifetime = content.ConfigureAwait(true);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(clientHttp2 ? HttpVersion.Version20 : HttpVersion.Version11, response.Version);
        Assert.Equal("HTTP/2", await protocol.Task.ConfigureAwait(true));
        Assert.Equal(body, await response.Content.ReadAsByteArrayAsync(deadline.Token).ConfigureAwait(true));
    }

    [Fact]
    public async Task AnOrdinaryHttp2ClientCanUploadBeyondThePeerWindowsAndReadTheResponseAsync()
    {
        using var certificate = DevelopmentUpstreamCertificate.Create();
        var upstream = await DevelopmentHttpUpstream.StartAsync(async context =>
        {
            using var body = new MemoryStream();
            await context.Request.Body.CopyToAsync(body, context.RequestAborted).ConfigureAwait(false);
            body.Position = 0;
            await body.CopyToAsync(context.Response.Body, context.RequestAborted).ConfigureAwait(false);
        }, certificate, http2: true).ConfigureAwait(true);
        await using var upstreamLifetime = upstream.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(upstream.Port, "svc.site.test", enrolledSite: true, upstreamHttp2: true).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var secure = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.TlsPort, "svc.site.test");
        var body = new byte[2 * 1024 * 1024];
        for (var index = 0; index < body.Length; index++) body[index] = (byte)(index % 251);
        using var content = new ByteArrayContent(body);
        using var response = await secure.Client.PostAsync(new Uri("/buffered", UriKind.Relative), content).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version20, response.Version);
        Assert.Equal(body, await response.Content.ReadAsByteArrayAsync().ConfigureAwait(true));
    }

    [Fact]
    public async Task AnActualHttp2UpstreamCanRejectBeforeTheLargeUploadCompletesAsync()
    {
        using var certificate = DevelopmentUpstreamCertificate.Create();
        var upstream = await DevelopmentHttpUpstream.StartAsync(async context =>
        {
            context.Response.StatusCode = 413;
            await context.Response.WriteAsync("rejected", context.RequestAborted).ConfigureAwait(false);
        }, certificate, http2: true).ConfigureAwait(true);
        await using var upstreamLifetime = upstream.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(upstream.Port, upstreamHttp2: true).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var body = new ByteArrayContent(new byte[2 * 1024 * 1024]);
        using var response = await proxy.Client.PostAsync(new Uri("/reject", UriKind.Relative), body).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("rejected", await response.Content.ReadAsStringAsync().ConfigureAwait(true));
    }
}

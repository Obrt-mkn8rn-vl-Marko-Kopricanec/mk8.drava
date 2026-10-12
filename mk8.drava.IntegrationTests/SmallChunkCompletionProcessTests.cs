using System.Net;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
public sealed class SmallChunkCompletionProcessTests
{
    [Fact]
    public async Task SustainedOneByteChunksReachEofWithoutMembershipChangesAsync()
    {
        var origin = await DevelopmentHttpUpstream.StartAsync(async context =>
        {
            for (var index = 0; index < 40; index++)
            {
                await context.Response.WriteAsync(".", context.RequestAborted).ConfigureAwait(false);
                await context.Response.Body.FlushAsync(context.RequestAborted).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds(1), context.RequestAborted).ConfigureAwait(false);
            }
            await context.Response.WriteAsync("k", context.RequestAborted).ConfigureAwait(false);
        }).ConfigureAwait(true);
        await using var originLifetime = origin.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(origin.Port, "svc.site.test", enrolledSite: true).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var client = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.TlsPort, "svc.site.test");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var response = await client.Client.GetAsync(new Uri("/sustained-chunks", UriKind.Relative),
            HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version20, response.Version);
        var body = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(true);
        var received = new byte[41];
        await body.ReadExactlyAsync(received, deadline.Token).ConfigureAwait(true);
        Assert.Equal(System.Text.Encoding.ASCII.GetBytes(new string('.', 40) + "k"), received);
        Assert.Equal(0, await body.ReadAsync(new byte[1], deadline.Token).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SeparatelyFlushedOneByteChunksReachExactBodyAndEofAsync(bool publicHttp2)
    {
        var origin = await DevelopmentHttpUpstream.StartAsync(async context =>
        {
            foreach (var value in "o.k")
            {
                await context.Response.WriteAsync(value.ToString(), context.RequestAborted).ConfigureAwait(false);
                await context.Response.Body.FlushAsync(context.RequestAborted).ConfigureAwait(false);
                await Task.Delay(100, context.RequestAborted).ConfigureAwait(false);
            }
        }).ConfigureAwait(true);
        await using var originLifetime = origin.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(origin.Port, "svc.site.test", enrolledSite: true).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var client = new DevelopmentSiteClient(proxy.RootCertificatePath, proxy.TlsPort, "svc.site.test");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/small-chunks", UriKind.Relative))
        {
            Version = publicHttp2 ? HttpVersion.Version20 : HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
        using var response = await client.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(request.Version, response.Version);
        var body = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(true);
        var bytes = new byte[3];
        await body.ReadExactlyAsync(bytes, deadline.Token).ConfigureAwait(true);
        Assert.Equal("o.k"u8.ToArray(), bytes);
        Assert.Equal(0, await body.ReadAsync(new byte[1], deadline.Token).ConfigureAwait(true));
    }
}

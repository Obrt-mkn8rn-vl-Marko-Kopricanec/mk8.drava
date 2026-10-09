using System.Net;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
public sealed class Http1UploadDeadlineProcessTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ActiveUploadPrecedesTheResponseWaitAndSilentOriginRemainsBoundedAsync(bool expectContinue, bool silent)
    {
        var completed = 0;
        var upstream = await DevelopmentHttpUpstream.StartAsync(async context =>
        {
            Assert.Equal(3L, context.Request.ContentLength);
            var body = new byte[3];
            await context.Request.Body.ReadExactlyAsync(body, context.RequestAborted).ConfigureAwait(false);
            Assert.Equal("abc"u8.ToArray(), body);
            Assert.Equal(0, await context.Request.Body.ReadAsync(new byte[1], context.RequestAborted).ConfigureAwait(false));
            Interlocked.Exchange(ref completed, 1);
            if (silent) await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted).ConfigureAwait(false);
            else await context.Response.WriteAsync("abc", context.RequestAborted).ConfigureAwait(false);
        }).ConfigureAwait(true);
        await using var upstreamLifetime = upstream.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(upstream.Port, responseHeadTimeoutMs: 750).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var content = new DevelopmentSlowKnownLengthContent();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/slow", UriKind.Relative)) { Content = content };
        request.Headers.ExpectContinue = expectContinue;
        using var response = await proxy.Client.SendAsync(request).ConfigureAwait(true);
        Assert.Equal(1, Volatile.Read(ref completed));
        Assert.Equal(silent ? HttpStatusCode.GatewayTimeout : HttpStatusCode.OK, response.StatusCode);
        if (!silent) Assert.Equal("abc", await response.Content.ReadAsStringAsync().ConfigureAwait(true));
    }
}

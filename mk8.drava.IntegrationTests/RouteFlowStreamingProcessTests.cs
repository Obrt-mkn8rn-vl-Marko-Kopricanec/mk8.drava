using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Mk8.Drava.Application.BLL.Configuration;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
public sealed class RouteFlowStreamingProcessTests
{
    [Fact]
    public async Task EventChunksFlushAndResetIdleBudgetAcrossTwoProcessesAsync()
    {
        TaskCompletionSource[] releases = [NewSignal(), NewSignal(), NewSignal()];
        string[] events = ["data: first\n\n", "data: second\n\n", "data: third\n\n"];
        var settled = NewSignal();
        var upstream = await DevelopmentHttpUpstream.StartAsync(async context =>
        {
            try
            {
                Assert.Equal("app.test", context.Request.Host.Value);
                context.Response.ContentType = "text/event-stream";
                for (var index = 0; index < events.Length; index++)
                {
                    await context.Response.WriteAsync(events[index], context.RequestAborted).ConfigureAwait(false);
                    await context.Response.Body.FlushAsync(context.RequestAborted).ConfigureAwait(false);
                    await releases[index].Task.WaitAsync(context.RequestAborted).ConfigureAwait(false);
                }
            }
            finally { settled.TrySetResult(); }
        }).ConfigureAwait(true);
        await using var upstreamLifetime = upstream.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(upstream.Port, flowTimeouts: Settings()).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/events", UriKind.Relative));
        using var response = await proxy.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(true);
        foreach (var index in Enumerable.Range(0, events.Length))
        {
            var expected = Encoding.UTF8.GetBytes(events[index]);
            var received = new byte[expected.Length];
            await body.ReadExactlyAsync(received, timeout.Token).ConfigureAwait(true);
            Assert.Equal(expected, received);
            Assert.False(settled.Task.IsCompleted);
            await Task.Delay(TimeSpan.FromMilliseconds(800), timeout.Token).ConfigureAwait(true);
            releases[index].TrySetResult();
        }
        Assert.Equal(0, await body.ReadAsync(new byte[1], timeout.Token).ConfigureAwait(true));
        await settled.Task.WaitAsync(timeout.Token).ConfigureAwait(true);
    }

    [Fact]
    public async Task OrdinaryRouteRetainsItsShortIdleBoundAsync()
    {
        var settled = NewSignal();
        var upstream = await DevelopmentHttpUpstream.StartAsync(async context =>
        {
            try
            {
                context.Response.ContentType = "text/event-stream";
                await context.Response.WriteAsync("data: first\n\n", context.RequestAborted).ConfigureAwait(false);
                await context.Response.Body.FlushAsync(context.RequestAborted).ConfigureAwait(false);
                await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted).ConfigureAwait(false);
            }
            finally { settled.TrySetResult(); }
        }).ConfigureAwait(true);
        await using var upstreamLifetime = upstream.ConfigureAwait(true);
        var proxy = await TwoProcessProxy.StartAsync(upstream.Port, flowTimeouts: Settings()).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/ordinary", UriKind.Relative));
        using var response = await proxy.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(true);
        var body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(true);
        await body.ReadExactlyAsync(new byte[13], timeout.Token).ConfigureAwait(true);
        await Assert.ThrowsAnyAsync<IOException>(async () => await body.ReadAsync(new byte[1], timeout.Token).ConfigureAwait(false)).ConfigureAwait(true);
        await settled.Task.WaitAsync(timeout.Token).ConfigureAwait(true);
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static DevelopmentFlowTimeoutSettings Settings() => new()
    {
        Global = new ProxyTimeoutOptions { UpstreamResponseBodyIdleTimeoutMs = 100 },
        Events = new ProxyRouteOverrideOptions { UpstreamResponseBodyIdleTimeoutMs = 2000 },
    };
}

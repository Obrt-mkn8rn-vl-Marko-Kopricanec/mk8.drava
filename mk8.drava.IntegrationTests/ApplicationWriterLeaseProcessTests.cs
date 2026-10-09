using System.Net;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[Collection(DevelopmentSubprocessTests.Name)]
public sealed class ApplicationWriterLeaseProcessTests
{
    [Fact]
    public async Task ASecondApplicationCannotReuseLiveStateOrDisruptThePrivateListenerAsync()
    {
        var proxy = await TwoProcessProxy.StartAsync(TwoProcessProxy.UnusedPort(), manualRoute: false).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        var bootstrap = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(proxy.ApplicationPlanPath)!, "..", "application.json"));
        var second = new DevelopmentProcess(DevelopmentBinaryPaths.ForProject("mk8.drava.Application"), bootstrap);
        await using var secondLifetime = second.ConfigureAwait(true);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.NotEqual(0, await second.WaitForExitAsync(deadline.Token).ConfigureAwait(true));
        await second.DisposeAsync().ConfigureAwait(true);
        Assert.Contains(OperatingSystem.IsLinux() ? "Private writer lease is unavailable" : "application.lock", second.CapturedLog, StringComparison.Ordinal);
        using var response = await proxy.Client.GetAsync(new Uri("/", UriKind.Relative), deadline.Token).ConfigureAwait(true);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RetiringTheApplicationAllowsItsStateAndPrivateListenerToBeReopenedAsync()
    {
        var proxy = await TwoProcessProxy.StartAsync(TwoProcessProxy.UnusedPort(), manualRoute: false).ConfigureAwait(true);
        await using var proxyLifetime = proxy.ConfigureAwait(true);
        using (var first = await proxy.Client.GetAsync(new Uri("/", UriKind.Relative)).ConfigureAwait(true)) Assert.Equal(HttpStatusCode.NotFound, first.StatusCode);
        await proxy.StopApplicationAsync().ConfigureAwait(true);
        await proxy.StartApplicationAsync().ConfigureAwait(true);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (true)
        {
            using var next = await proxy.Client.GetAsync(new Uri("/", UriKind.Relative), deadline.Token).ConfigureAwait(true);
            if (next.StatusCode != HttpStatusCode.ServiceUnavailable)
            {
                Assert.Equal(HttpStatusCode.NotFound, next.StatusCode);
                return;
            }
            await Task.Delay(50, deadline.Token).ConfigureAwait(true);
        }
    }
}

using Microsoft.Extensions.Logging.Abstractions;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.INF.Proxy.Forwarding;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class TunnelOwnedShutdownTests
{
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    public async Task FailureJoinsBothDirectionsAndSurvivesOwnerCancellationAsync(bool clientFails, bool ownerCancels, bool callbackFails)
    {
        Exception failure = callbackFails ? new IOException("Controlled cancellation callback failure.") : new InvalidOperationException("Controlled relay failure.");
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var failing = new DevelopmentTunnelReadGateStream(callbackFails ? null : failure, guard.Token);
        await using var failingLifetime = failing.ConfigureAwait(true);
        var pending = new DevelopmentTunnelReadGateStream(failure: null, guard.Token, callbackFails ? failure : null);
        await using var pendingLifetime = pending.ConfigureAwait(true);
        using var cancellation = new CancellationTokenSource();
        var relay = new TunnelRelay(new ProxyMetrics(), NullLogger<TunnelRelay>.Instance, TimeProvider.System);
        var listener = ProxyConfigurationRuntimeMapper.ToRuntimeListeners([new ListenerOptions { Name = "fixture", ForwardingBufferBytes = 4096 }])[0];
        var timeouts = RuntimeTimeoutsFactory.ForHealthCheck(TimeSpan.FromSeconds(30));
        var operation = relay.RelayAsync(clientFails ? failing : pending, clientFails ? pending : failing,
            listener, timeouts, cancellation.Token).AsTask();
        try
        {
            await Task.WhenAll(failing.Entered.Task, pending.Entered.Task).WaitAsync(TimeSpan.FromSeconds(5),
                TimeProvider.System, cancellation.Token).ConfigureAwait(true);
            failing.Release();
            await pending.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5), TimeProvider.System, cancellation.Token).ConfigureAwait(true);
            await Assert.ThrowsAsync<TimeoutException>(() => operation.WaitAsync(TimeSpan.FromMilliseconds(100),
                TimeProvider.System, CancellationToken.None)).ConfigureAwait(true);
            Assert.False(operation.IsCompleted);
            Assert.False(pending.Settled.Task.IsCompleted);
            if (ownerCancels) await cancellation.CancelAsync().ConfigureAwait(true);
            pending.Release();
            await RequireFailureAsync(operation, failure, callbackFails).ConfigureAwait(true);
            Assert.True(failing.Settled.Task.IsCompletedSuccessfully);
            Assert.True(pending.Settled.Task.IsCompletedSuccessfully);
        }
        finally
        {
            failing.Release();
            pending.Release();
            await cancellation.CancelAsync().ConfigureAwait(true);
            try { await operation.ConfigureAwait(true); }
            catch (InvalidOperationException exception) when (ReferenceEquals(exception, failure)) { }
            catch (AggregateException exception) when (callbackFails && exception.Flatten().InnerExceptions.All(inner => ReferenceEquals(inner, failure))) { }
            await Task.WhenAll(failing.Settled.Task, pending.Settled.Task).WaitAsync(TimeSpan.FromSeconds(5),
                TimeProvider.System, CancellationToken.None).ConfigureAwait(true);
        }
    }

    private static async Task RequireFailureAsync(Task operation, Exception failure, bool callbackFails)
    {
        if (callbackFails)
        {
            var observed = await Assert.ThrowsAsync<AggregateException>(() => operation.WaitAsync(TimeSpan.FromSeconds(5),
                TimeProvider.System, CancellationToken.None)).ConfigureAwait(true);
#pragma warning disable HLQ005 // xUnit Assert.Single verifies exactly one failure; First would weaken the required cardinality assertion.
            Assert.Same(failure, Assert.Single(observed.Flatten().InnerExceptions));
#pragma warning restore HLQ005
        }
        else
        {
            var observed = await Assert.ThrowsAsync<InvalidOperationException>(() => operation.WaitAsync(TimeSpan.FromSeconds(5),
                TimeProvider.System, CancellationToken.None)).ConfigureAwait(true);
            Assert.Same(failure, observed);
        }
    }
}

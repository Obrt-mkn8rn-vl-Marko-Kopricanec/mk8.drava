using Microsoft.Extensions.Logging.Abstractions;
using Mk8.Drava.Application.BLL.NoConf;
using Mk8.Drava.Application.INF.NoConf;
using Mk8.Drava.Application.INF.Runtime;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class NoConfShutdownTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShutdownJoinsActiveAndQueuedAdministrationAfterAStopWaiterCancelsAsync(bool synchronousDispose)
    {
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var fixture = new RegistryTestFixture();
        await fixture.InitializeAsync().ConfigureAwait(true);
        var control = new DevelopmentReconcilerControl(guard.Token) { HoldPolicyRead = true };
        var service = Create(fixture, control);
        await using var serviceLifetime = service.ConfigureAwait(true);
        var reads = new List<Task<NoConfPolicyView>>();
        try
        {
            reads.Add(service.ReadPolicyViewAsync(includeHistory: true, guard.Token).AsTask());
            await control.Entered.Task.WaitAsync(guard.Token).ConfigureAwait(true);
            reads.Add(service.ReadPolicyViewAsync(includeHistory: true, guard.Token).AsTask());
            await CancelQueuedReadAsync(service, guard.Token).ConfigureAwait(true);
            if (synchronousDispose) DisposeSynchronously(service);
            using var waiter = new CancellationTokenSource();
            var first = service.StopAsync(waiter.Token);
            var second = service.StopAsync(guard.Token);
            Assert.False(second.IsCompleted);
            Assert.All(reads, static read => Assert.False(read.IsCompleted));
            await AssertAdmissionsClosedAsync(service).ConfigureAwait(true);
            await waiter.CancelAsync().ConfigureAwait(true);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(guard.Token)).ConfigureAwait(true);
            Assert.False(second.IsCompleted);
            control.Release.TrySetResult();
            var results = await Task.WhenAll(reads).WaitAsync(guard.Token).ConfigureAwait(true);
            await second.ConfigureAwait(true);
            Assert.All(results, static view => Assert.Equal(1, view.Accepted?.Revision));
            Assert.Equal(2, control.HistoryReads);
            await service.StopAsync(guard.Token).ConfigureAwait(true);
        }
        finally { control.Release.TrySetResult(); await Task.WhenAll(reads).WaitAsync(guard.Token).ConfigureAwait(true); }
    }

    [Fact]
    public async Task ShutdownDuringInitializationJoinsCompilationAndPreventsBackgroundStartupAsync()
    {
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var fixture = new RegistryTestFixture();
        await fixture.InitializeAsync().ConfigureAwait(true);
        var control = new DevelopmentReconcilerControl(guard.Token) { HoldSourceRead = true };
        var service = Create(fixture, control);
        await using var serviceLifetime = service.ConfigureAwait(true);
        var start = service.StartAsync(guard.Token);
        try
        {
            await control.Entered.Task.WaitAsync(guard.Token).ConfigureAwait(true);
            var stop = service.StopAsync(guard.Token);
            Assert.False(stop.IsCompleted);
            control.Release.TrySetResult();
            await Assert.ThrowsAsync<ObjectDisposedException>(() => start.WaitAsync(guard.Token)).ConfigureAwait(true);
            await stop.ConfigureAwait(true);
            Assert.Null(service.ExecuteTask);
        }
        finally
        {
            control.Release.TrySetResult();
            try { await start.WaitAsync(guard.Token).ConfigureAwait(true); }
            catch (ObjectDisposedException) { }
        }
    }

    [Fact]
    public async Task ShutdownWaitsForACancellationIgnoringReadinessProbeAsync()
    {
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var fixture = new RegistryTestFixture();
        await fixture.InitializeAsync().ConfigureAwait(true);
        await fixture.ReadyAsync(RegistryTestFixture.Intent()).ConfigureAwait(true);
        var control = new DevelopmentReconcilerControl(guard.Token) { HoldProbe = true };
        var service = Create(fixture, control);
        await using var serviceLifetime = service.ConfigureAwait(true);
        try
        {
            await service.StartAsync(guard.Token).ConfigureAwait(true);
            await control.Entered.Task.WaitAsync(guard.Token).ConfigureAwait(true);
            var stop = service.StopAsync(guard.Token);
            var disposal = service.DisposeAsync().AsTask();
            await control.Canceled.Task.WaitAsync(guard.Token).ConfigureAwait(true);
            Assert.False(stop.IsCompleted);
            Assert.False(disposal.IsCompleted);
            control.Release.TrySetResult();
            await Task.WhenAll(stop, disposal).WaitAsync(guard.Token).ConfigureAwait(true);
            Assert.True(service.ExecuteTask?.IsCompleted);
        }
        finally { control.Release.TrySetResult(); }
    }

    [Fact]
    public async Task ConcurrentShutdownAndAsyncDisposalObserveTheSameUnexpectedExecutionFailureAsync()
    {
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var fixture = new RegistryTestFixture();
        await fixture.InitializeAsync().ConfigureAwait(true);
        await fixture.ReadyAsync(RegistryTestFixture.Intent()).ConfigureAwait(true);
        var failure = new InvalidOperationException("Controlled readiness failure.");
        var control = new DevelopmentReconcilerControl(guard.Token) { HoldProbe = true, Failure = failure };
        var joined = false;
        var disposalFailure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            var service = Create(fixture, control);
            await using var serviceLifetime = service.ConfigureAwait(true);
            try
            {
                await service.StartAsync(guard.Token).ConfigureAwait(true);
                await control.Entered.Task.WaitAsync(guard.Token).ConfigureAwait(true);
                control.Release.TrySetResult();
                Assert.NotNull(service.ExecuteTask);
                Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteTask.WaitAsync(guard.Token)).ConfigureAwait(true));
                var first = service.StopAsync(guard.Token);
                var second = service.StopAsync(guard.Token);
                Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => first.WaitAsync(guard.Token)).ConfigureAwait(true));
                Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => second.WaitAsync(guard.Token)).ConfigureAwait(true));
                joined = true;
            }
            finally { control.Release.TrySetResult(); }
        }).ConfigureAwait(true);
        Assert.True(joined);
        Assert.Same(failure, disposalFailure);
    }

    private static NoConfReconciler Create(RegistryTestFixture fixture, DevelopmentReconcilerControl control)
    {
        var store = new ProxyConfigurationStore();
        store.Replace(NoConfCompilerTests.Baseline());
        return new NoConfReconciler(fixture.Registry, fixture.Availability, store, NoConfCompilerTests.Compiler(), control, control, control,
            control, "site.example", "node", fixture.Clock, NullLogger<NoConfReconciler>.Instance, control);
    }

    private static async Task CancelQueuedReadAsync(NoConfReconciler service, CancellationToken guard)
    {
        using var cancellation = new CancellationTokenSource();
        var read = service.ReadPolicyViewAsync(includeHistory: true, cancellation.Token).AsTask();
        await cancellation.CancelAsync().ConfigureAwait(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.WaitAsync(guard)).ConfigureAwait(true);
    }

    private static async Task AssertAdmissionsClosedAsync(NoConfReconciler service)
    {
        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.ReadPolicyViewAsync(includeHistory: false, CancellationToken.None).AsTask()).ConfigureAwait(true);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.UpdatePolicyAsync(1, "{}", importFile: false, CancellationToken.None).AsTask()).ConfigureAwait(true);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.RollbackPolicyAsync(1, 1, CancellationToken.None).AsTask()).ConfigureAwait(true);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.ApplyManualBaselineAsync(NoConfCompilerTests.Baseline(), static snapshot => snapshot, CancellationToken.None).AsTask()).ConfigureAwait(true);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.InitializeAsync(CancellationToken.None).AsTask()).ConfigureAwait(true);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.StartAsync(CancellationToken.None)).ConfigureAwait(true);
    }

    private static void DisposeSynchronously(NoConfReconciler service) => service.Dispose();
}

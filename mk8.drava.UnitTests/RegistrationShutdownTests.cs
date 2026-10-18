using Microsoft.Extensions.Hosting;
using Mk8.Drava.Configuration;
using Mk8.Drava.Contracts.Registration.V1;
using Mk8.Drava.Registration;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class RegistrationShutdownTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task StopWaitersJoinRenewalThenOneDrainAndDisposalAsync(int stopMode)
    {
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var lifetime = new ApplicationLifetime();
        var state = new DravaRegistrationState();
        state.Accept(Ready());
        using var session = new ControlledSession(guard.Token);
        var service = new DravaRegistrationHostedService(new DravaRegistrationOptions(), state, lifetime, () => session);
        await using var serviceLifetime = service.ConfigureAwait(true);
        using var canceledWaiter = new CancellationTokenSource();
        try
        {
            await service.StartAsync(guard.Token).ConfigureAwait(true);
            await lifetime.StartAsync().ConfigureAwait(true);
            await session.RenewEntered.Task.WaitAsync(guard.Token).ConfigureAwait(true);
            if (stopMode == 1) DisposeSynchronously(service);
            if (stopMode == 2)
            {
                lifetime.StopApplication();
                await session.RenewCanceled.Task.WaitAsync(guard.Token).ConfigureAwait(true);
                Assert.Null(state.Status);
            }
            var first = service.StopAsync(canceledWaiter.Token);
            var second = service.StopAsync(guard.Token);
            await session.RenewCanceled.Task.WaitAsync(guard.Token).ConfigureAwait(true);
            Assert.Null(state.Status);
            Assert.False(second.IsCompleted);
            Assert.Equal(0, session.DrainCount);
            Assert.Equal(0, session.DisposeCount);
            await canceledWaiter.CancelAsync().ConfigureAwait(true);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(guard.Token)).ConfigureAwait(true);
            Assert.False(second.IsCompleted);
            session.ReleaseRenew.TrySetResult();
            await session.DrainEntered.Task.WaitAsync(guard.Token).ConfigureAwait(true);
            Assert.Null(state.Status);
            Assert.Equal(1, session.DrainCount);
            Assert.Equal(0, session.DisposeCount);
            session.ReleaseDrain.TrySetResult();
            await second.ConfigureAwait(true);
            await service.StopAsync(guard.Token).ConfigureAwait(true);
            Assert.Equal(RegistrationPhase.Draining, state.Status?.Phase);
            Assert.Equal(1, session.DrainCount);
            Assert.Equal(1, session.DisposeCount);
        }
        finally { session.Release(); }
    }

    [Fact]
    public async Task ExpiredShutdownBudgetStillJoinsRenewalWithoutStartingDrainAsync()
    {
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var lifetime = new ApplicationLifetime();
        var clock = new RegistryTimeProvider();
        var state = new DravaRegistrationState(clock);
        using var session = new ControlledSession(guard.Token);
        var service = new DravaRegistrationHostedService(new DravaRegistrationOptions(), state, lifetime, () => session, clock);
        await using var serviceLifetime = service.ConfigureAwait(true);
        try
        {
            await service.StartAsync(guard.Token).ConfigureAwait(true);
            await lifetime.StartAsync().ConfigureAwait(true);
            await session.RenewEntered.Task.WaitAsync(guard.Token).ConfigureAwait(true);
            var stop = service.StopAsync(guard.Token);
            await session.RenewCanceled.Task.WaitAsync(guard.Token).ConfigureAwait(true);
            clock.Advance(TimeSpan.FromSeconds(4));
            Assert.False(stop.IsCompleted);
            Assert.Equal(0, session.DisposeCount);
            session.ReleaseRenew.TrySetResult();
            await stop.ConfigureAwait(true);
            Assert.Equal(0, session.DrainCount);
            Assert.Equal(1, session.DisposeCount);
            Assert.Null(state.Status);
            Assert.NotEmpty(state.Failure);
        }
        finally { session.Release(); }
    }

    [Fact]
    public async Task StopBeforeApplicationStartedDoesNotCreateASessionAsync()
    {
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var lifetime = new ApplicationLifetime();
        var sessions = 0;
        var service = new DravaRegistrationHostedService(new DravaRegistrationOptions(), new DravaRegistrationState(), lifetime,
            () => { Interlocked.Increment(ref sessions); throw new InvalidOperationException("Registration must wait for bound listeners."); });
        await using var serviceLifetime = service.ConfigureAwait(true);
        await service.StartAsync(guard.Token).ConfigureAwait(true);
        await service.StopAsync(guard.Token).ConfigureAwait(true);
        await lifetime.StartAsync().ConfigureAwait(true);
        Assert.Equal(0, Volatile.Read(ref sessions));
    }

    [Fact]
    public async Task EveryStopWaiterReceivesTheSameUnexpectedFailureAfterCleanupAsync()
    {
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var lifetime = new ApplicationLifetime();
        var failure = new InvalidOperationException("Controlled renewal failure.");
        using var session = new ControlledSession(guard.Token, failure);
        var stopCallsJoined = false;
        var disposalFailure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            var service = new DravaRegistrationHostedService(new DravaRegistrationOptions(), new DravaRegistrationState(), lifetime, () => session);
            await using var serviceLifetime = service.ConfigureAwait(true);
            try
            {
                await service.StartAsync(guard.Token).ConfigureAwait(true);
                await lifetime.StartAsync().ConfigureAwait(true);
                await session.RenewEntered.Task.WaitAsync(guard.Token).ConfigureAwait(true);
                var first = service.StopAsync(guard.Token);
                var second = service.StopAsync(guard.Token);
                await session.RenewCanceled.Task.WaitAsync(guard.Token).ConfigureAwait(true);
                session.Release();
                Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => first.WaitAsync(guard.Token)).ConfigureAwait(true));
                Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => second.WaitAsync(guard.Token)).ConfigureAwait(true));
                stopCallsJoined = true;
            }
            finally { session.Release(); }
        }).ConfigureAwait(true);
        Assert.True(stopCallsJoined);
        Assert.Same(failure, disposalFailure);
        Assert.Equal(1, session.DrainCount);
        Assert.Equal(1, session.DisposeCount);
    }

    private static void DisposeSynchronously(DravaRegistrationHostedService service) => service.Dispose();

    private static RegistrationStatus Ready() => new()
    {
        Phase = RegistrationPhase.Ready, LeaseSeconds = 90, RenewAfterSeconds = 30, AssignedUrls = ["https://svc.site.test/"],
    };

    private sealed class ControlledSession(CancellationToken guard, Exception? renewalFailure = null) : IRegistrationSession
    {
        private int _drains;
        private int _disposals;
        public TaskCompletionSource RenewEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RenewCanceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseRenew { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource DrainEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseDrain { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int DrainCount => Volatile.Read(ref _drains);
        public int DisposeCount => Volatile.Read(ref _disposals);
        public NodeAgentDescriptor? AgentDescriptor => null;

        public async ValueTask<RegistrationStatus?> RenewAsync(CancellationToken cancellationToken)
        {
            var canceled = cancellationToken.Register(() => RenewCanceled.TrySetResult());
            await using var canceledLifetime = canceled.ConfigureAwait(false);
            RenewEntered.TrySetResult();
            await ReleaseRenew.Task.WaitAsync(guard).ConfigureAwait(false);
            if (renewalFailure is not null) throw renewalFailure;
            return Ready();
        }

        public async ValueTask<RegistrationStatus?> DrainAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _drains);
            DrainEntered.TrySetResult();
            await ReleaseDrain.Task.WaitAsync(guard).WaitAsync(cancellationToken).ConfigureAwait(false);
            return new RegistrationStatus { Phase = RegistrationPhase.Draining, LeaseSeconds = 90 };
        }

        public void Release() { ReleaseRenew.TrySetResult(); ReleaseDrain.TrySetResult(); }
        public void Dispose() => Interlocked.Increment(ref _disposals);
    }

    private sealed class ApplicationLifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource _started = new();
        private readonly CancellationTokenSource _stopping = new();
        private readonly CancellationTokenSource _stopped = new();
        public CancellationToken ApplicationStarted => _started.Token;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => _stopped.Token;
        public Task StartAsync() => _started.CancelAsync();
        public void StopApplication() => _stopping.Cancel();
        public void Dispose() { _started.Dispose(); _stopping.Dispose(); _stopped.Dispose(); }
    }
}

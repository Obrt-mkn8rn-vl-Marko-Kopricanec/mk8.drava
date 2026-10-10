using Mk8.Drava.Application.BLL.ControlPlane.RuntimeGuards;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class ProxyShutdownCoordinatorTests
{
    [Fact]
    public async Task TokenCapturedBeforeShutdownCancelsAtTheGraceDeadlineAsync()
    {
        var clock = new DevelopmentOperationDeadlineClock();
        using var coordinator = new ProxyShutdownCoordinator(clock);
        var captured = coordinator.Token;
        Assert.True(captured.CanBeCanceled);
        Assert.False(captured.IsCancellationRequested);

        Assert.Equal(captured, coordinator.BeginShutdown(TimeSpan.FromMilliseconds(50)));
        using var cleanup = CancellationTokenSource.CreateLinkedTokenSource(captured);
        var pending = Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, cleanup.Token);
        try
        {
            clock.Advance(TimeSpan.FromMilliseconds(49));
            Assert.False(pending.IsCompleted);
            clock.Advance(TimeSpan.FromMilliseconds(1));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5), TimeProvider.System, CancellationToken.None)).ConfigureAwait(true);
            Assert.True(captured.IsCancellationRequested);
        }
        finally
        {
            await cleanup.CancelAsync().ConfigureAwait(true);
            try { await pending.ConfigureAwait(true); }
            catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public void RepeatedShutdownKeepsTheCapturedTokenAndFirstDeadline()
    {
        var clock = new DevelopmentOperationDeadlineClock();
        using var coordinator = new ProxyShutdownCoordinator(clock);
        var captured = coordinator.Token;
        var first = coordinator.BeginShutdown(TimeSpan.FromMilliseconds(50));
        var started = coordinator.StartedAtUtc;
        var deadline = coordinator.DeadlineUtc;
        clock.Advance(TimeSpan.FromMilliseconds(49));
        var second = coordinator.BeginShutdown(TimeSpan.FromMinutes(1));

        Assert.Equal(captured, first);
        Assert.Equal(first, second);
        Assert.Equal(started, coordinator.StartedAtUtc);
        Assert.Equal(deadline, coordinator.DeadlineUtc);
        Assert.False(captured.IsCancellationRequested);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(captured.IsCancellationRequested);
    }

    [Theory]
    [InlineData(-2L)]
    [InlineData(4294967295L)]
    public void InvalidGraceDoesNotPublishShutdownState(long milliseconds)
    {
        var clock = new DevelopmentOperationDeadlineClock();
        using var coordinator = new ProxyShutdownCoordinator(clock);
        var captured = coordinator.Token;

        Assert.Throws<ArgumentOutOfRangeException>(() => coordinator.BeginShutdown(TimeSpan.FromMilliseconds(milliseconds)));
        Assert.False(coordinator.IsShuttingDown);
        Assert.Null(coordinator.StartedAtUtc);
        Assert.Null(coordinator.DeadlineUtc);
        Assert.Equal(captured, coordinator.Token);
        Assert.False(captured.IsCancellationRequested);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisposedCoordinatorCannotResumeOrIssueTokens(bool started)
    {
        var clock = new DevelopmentOperationDeadlineClock();
        using var coordinator = new ProxyShutdownCoordinator(clock);
        if (started) coordinator.BeginShutdown(TimeSpan.FromMilliseconds(50));

        coordinator.Dispose();
        coordinator.Dispose();
        Assert.Throws<ObjectDisposedException>(() => coordinator.Token);
        Assert.Throws<ObjectDisposedException>(() => coordinator.BeginShutdown(TimeSpan.FromMilliseconds(50)));
    }
}

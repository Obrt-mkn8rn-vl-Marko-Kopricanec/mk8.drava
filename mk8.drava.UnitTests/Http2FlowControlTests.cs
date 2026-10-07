using Mk8.Drava.Application.INF.Proxy.Http2;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class Http2FlowControlTests
{
    [Fact]
    public async Task BothWindowsMustHaveCreditAndSettingsCanMakeTheStreamNegativeAsync()
    {
        var flow = new Http2UpstreamFlowControl();
        Assert.Equal(16384, await flow.ReserveAsync(65535, CancellationToken.None).ConfigureAwait(true));
        flow.SetInitialWindow(0);
        using var cancellation = new CancellationTokenSource();
        var blocked = flow.ReserveAsync(1, cancellation.Token).AsTask();
        flow.AddCredit(true, 1);
        flow.AddCredit(false, 16384);
        Assert.False(blocked.IsCompleted);
        flow.AddCredit(false, 1);
        Assert.Equal(1, await blocked.ConfigureAwait(true));
    }

    [Fact]
    public async Task CancellationLeavesWindowCreditAvailableAndFailureReleasesWaitersAsync()
    {
        var flow = new Http2UpstreamFlowControl();
        flow.SetInitialWindow(0);
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            var blocked = flow.ReserveAsync(1, cancellation.Token).AsTask();
            await cancellation.CancelAsync().ConfigureAwait(true);
            await blocked.ConfigureAwait(true);
        }).ConfigureAwait(true);
        flow.AddCredit(false, 1);
        Assert.Equal(1, await flow.ReserveAsync(1, CancellationToken.None).ConfigureAwait(true));
        await Assert.ThrowsAsync<IOException>(async () =>
        {
            var failed = flow.ReserveAsync(1, CancellationToken.None).AsTask();
            flow.Fail(new IOException("peer failed"));
            await failed.ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(true, 0u)]
    [InlineData(false, 0u)]
    [InlineData(true, 2147483647u)]
    [InlineData(false, 2147483647u)]
    [InlineData(true, 2147483648u)]
    public void ZeroAndOverflowingCreditsAreRejected(bool connection, uint increment)
    {
        var flow = new Http2UpstreamFlowControl();
        Assert.Throws<Http2UpstreamProtocolException>(() => flow.AddCredit(connection, increment));
    }
}

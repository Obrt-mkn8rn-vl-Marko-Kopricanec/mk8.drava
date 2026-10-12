using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.BLL.ControlPlane.Timeouts;
using Mk8.Drava.Application.INF.Proxy.Forwarding;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class Http1BodyLineTests
{
    [Fact]
    public async Task ALineStopsAtCrlfWithoutConsumingTheFollowingPayloadAsync()
    {
        var source = new MemoryStream("3\r\nabc\r\n"u8.ToArray());
        await using var sourceLifetime = source.ConfigureAwait(true);
        var reader = new Http1BodyReader(source, "3"u8.ToArray(), new ProxyMetrics(), TimeSpan.FromSeconds(1), ProxyTimeoutKind.ClientRequestBodyIdle);
        Assert.Equal("33\r\n"u8.ToArray(), await reader.ReadLineWithCrlfAsync(4, CancellationToken.None).ConfigureAwait(true));
        Assert.Equal("abc"u8.ToArray(), await reader.ReadExactAsync(3, CancellationToken.None).ConfigureAwait(true));
    }

    [Theory]
    [InlineData("ab\r\n", 4, true)]
    [InlineData("abc\r\n", 4, false)]
    [InlineData("ab\r", 4, false)]
    public async Task LineLengthAndPrematureEofKeepTheirRejectionBoundsAsync(string input, int maximum, bool accepted)
    {
        var source = new MemoryStream(System.Text.Encoding.ASCII.GetBytes(input));
        await using var sourceLifetime = source.ConfigureAwait(true);
        var reader = new Http1BodyReader(source, default, new ProxyMetrics(), TimeSpan.FromSeconds(1), ProxyTimeoutKind.ClientRequestBodyIdle);
        if (accepted) Assert.Equal(System.Text.Encoding.ASCII.GetBytes(input), await reader.ReadLineWithCrlfAsync(maximum, CancellationToken.None).ConfigureAwait(true));
        else await Assert.ThrowsAsync<IOException>(async () => await reader.ReadLineWithCrlfAsync(maximum, CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003", Justification = "This test starts the read task and owns its cancellation source. Assertions and finally join the same read before disposing the source stream.")]
    public async Task ABlockedLineReadSettlesOnIdleTimeoutOrCallerCancellationAsync(bool callerCancellation)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var source = new WaitingBody();
        await using var sourceLifetime = source.ConfigureAwait(true);
        var timeout = callerCancellation ? TimeSpan.FromSeconds(2) : TimeSpan.FromMilliseconds(100);
        var reader = new Http1BodyReader(source, default, new ProxyMetrics(), timeout, ProxyTimeoutKind.ClientRequestBodyIdle);
        var read = reader.ReadLineWithCrlfAsync(8, stop.Token).AsTask();
        try
        {
            await source.Entered.Task.WaitAsync(stop.Token).ConfigureAwait(true);
            if (callerCancellation)
            {
                await stop.CancelAsync().ConfigureAwait(true);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read).ConfigureAwait(true);
            }
            else
            {
                var error = await Assert.ThrowsAsync<ProxyTimeoutException>(() => read.WaitAsync(stop.Token)).ConfigureAwait(true);
                Assert.Equal(ProxyTimeoutKind.ClientRequestBodyIdle, error.Kind);
                Assert.Equal(timeout, error.Timeout);
            }
            Assert.True(source.Exited.Task.IsCompletedSuccessfully);
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(true);
            try { await read.ConfigureAwait(true); }
            catch (Exception exception) when (exception is OperationCanceledException or ProxyTimeoutException) { }
        }
    }

    private sealed class WaitingBody : MemoryStream
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false); }
            finally { Exited.TrySetResult(); }
            return 0;
        }
    }
}

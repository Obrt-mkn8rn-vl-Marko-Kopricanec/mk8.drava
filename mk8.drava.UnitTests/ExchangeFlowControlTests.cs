using Google.Protobuf;
using Mk8.Drava.Transport.Protocol.V1;
using Mk8.Drava.Transport.Streaming;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class ExchangeFlowControlTests
{
    [Fact]
    public async Task SlowConsumerPreventsAnotherFrameUntilConsumptionCreditAsync()
    {
        var captured = new ExchangeFrameWriter();
        using var writer = new ExchangeServerWriter(captured, 1, CancellationToken.None);
        var frame = new ExchangeFrame { Data = new DataFrame { Payload = ByteString.CopyFromUtf8("first") } };
        await writer.WriteAsync(frame).ConfigureAwait(true);
        var blocked = writer.WriteAsync(frame);
        Assert.False(blocked.IsCompleted);
        Assert.Collection(captured.Frames, value => Assert.Same(frame, value));
        writer.ResponseWindow.Return(1);
        await blocked.ConfigureAwait(true);
        Assert.Equal(2, captured.Frames.Count);
        writer.ResponseWindow.Return(1);
        Assert.Throws<InvalidDataException>(() => writer.ResponseWindow.Return(1));
    }

    [Fact]
    public async Task CancellationReleasesABlockedSenderWithoutFabricatingCreditAsync()
    {
        using var window = new FrameWindow(1);
        await window.TakeAsync(CancellationToken.None).ConfigureAwait(true);
        using var stop = new CancellationTokenSource();
        var blocked = window.TakeAsync(stop.Token).AsTask();
        Assert.False(blocked.IsCompleted);
        await stop.CancelAsync().ConfigureAwait(true);
        try { await blocked.ConfigureAwait(true); Assert.Fail("Expected the blocked sender to observe cancellation."); }
        catch (OperationCanceledException) { }
        window.Return(1);
        Assert.Throws<InvalidDataException>(() => window.Return(1));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(2u)]
    public async Task RejectsFabricatedConsumptionCreditAsync(uint credit)
    {
        using var window = new FrameWindow(1);
        await window.TakeAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Throws<InvalidDataException>(() => window.Return(credit));
    }

    [Fact]
    public async Task InboundRequestFloodCannotExceedNegotiatedWindowAsync()
    {
        var frame = new ExchangeFrame { Data = new DataFrame { Payload = ByteString.CopyFromUtf8("x") } };
        using var reader = new ExchangeFrameReader([frame, frame, frame]);
        using var writer = new ExchangeServerWriter(new ExchangeFrameWriter(), 2, CancellationToken.None);
        var inbound = new ExchangeInbound(reader, writer, 2);
        await Assert.ThrowsAsync<InvalidDataException>(() => inbound.PumpAsync(CancellationToken.None)).ConfigureAwait(true);
    }
}

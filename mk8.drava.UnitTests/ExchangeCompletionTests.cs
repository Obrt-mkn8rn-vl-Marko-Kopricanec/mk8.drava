using Mk8.Drava.Transport.Protocol.V1;
using Mk8.Drava.Transport.Streaming;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class ExchangeCompletionTests
{
    [Fact]
    public async Task UploadEofBeforeResponseCompletionIsAFailureAsync()
    {
        using var reader = new ExchangeFrameReader([new ExchangeFrame { Complete = new Completion() }]);
        using var writer = new ExchangeServerWriter(new ExchangeFrameWriter(), 4, CancellationToken.None);
        var inbound = new ExchangeInbound(reader, writer, 4);
        await Assert.ThrowsAsync<EndOfStreamException>(() => inbound.PumpAsync(CancellationToken.None)).ConfigureAwait(true);
    }

    [Fact]
    public async Task CompletedUploadMayCloseAfterResponseCompletionWasAuthorizedAsync()
    {
        using var reader = new ExchangeFrameReader([new ExchangeFrame { Complete = new Completion() }]);
        using var writer = new ExchangeServerWriter(new ExchangeFrameWriter(), 4, CancellationToken.None);
        var inbound = new ExchangeInbound(reader, writer, 4);
        inbound.AllowClientCompletion();
        await inbound.PumpAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.True(await inbound.MoveNext(CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(ExchangeFrame.FrameOneofCase.Complete, inbound.Current.FrameCase);
        Assert.False(await inbound.MoveNext(CancellationToken.None).ConfigureAwait(true));
    }

    [Fact]
    public async Task EvenAnAuthorizedCompletionRequiresAnExplicitUploadTerminalFrameAsync()
    {
        using var reader = new ExchangeFrameReader([]);
        using var writer = new ExchangeServerWriter(new ExchangeFrameWriter(), 4, CancellationToken.None);
        var inbound = new ExchangeInbound(reader, writer, 4);
        inbound.AllowClientCompletion();
        await Assert.ThrowsAsync<EndOfStreamException>(() => inbound.PumpAsync(CancellationToken.None)).ConfigureAwait(true);
    }
}

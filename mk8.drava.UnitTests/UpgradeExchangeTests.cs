using Google.Protobuf;
using Mk8.Drava.Application.INF.Proxy.Exchange;
using Mk8.Drava.Transport.Protocol;
using Mk8.Drava.Transport.Protocol.V1;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class UpgradeExchangeTests
{
    private static ReadOnlySpan<byte> AcceptedHead => "HTTP/1.1 101 Switching Protocols\r\nConnection: Upgrade\r\nUpgrade: mk8-echo\r\n\r\n"u8;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcceptedUpgradeConsumesOpaqueBytesAndVerifiedCompletionAsync(bool declaredEmptyBody)
    {
        byte[] payload = [0, 255, 13, 10, 1, 2, 3, 4, 128];
        using var digest = new BodyDigest();
        digest.Append(payload);
        using var reader = new ExchangeFrameReader([
            new ExchangeFrame { Data = new DataFrame { Payload = ByteString.CopyFrom(payload, 0, 3) } },
            new ExchangeFrame { Data = new DataFrame { Payload = ByteString.CopyFrom(payload, 3, payload.Length - 3) } },
            new ExchangeFrame { Complete = digest.Complete() }]);
        var writer = new ExchangeFrameWriter();
        var head = new RequestHead { Method = "GET", WantsUpgrade = true };
        if (declaredEmptyBody) head.ContentLength = 0;
        var consumed = 0;
        using var stream = new ExchangeClientStream(reader, writer, head, _ => { consumed++; return ValueTask.CompletedTask; });
        await stream.WriteAsync(AcceptedHead.ToArray(), CancellationToken.None).ConfigureAwait(true);
        var received = new byte[payload.Length];
        await stream.ReadExactlyAsync(received, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(payload, received);
        Assert.Equal(0, await stream.ReadAsync(new byte[1], CancellationToken.None).ConfigureAwait(true));
        Assert.True(stream.UploadCompleted);
        Assert.Equal(2, consumed);
        await stream.CompleteAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.True(Assert.IsType<ResponseHead>(writer.Frames[0].Response).Upgrade);
        Assert.NotNull(writer.Frames[^1].Complete);
        Assert.DoesNotContain(writer.Frames, static frame => frame.StopUpload is not null);
    }

    [Fact]
    public async Task UpgradeBytesCannotBeReadBeforeAcceptanceAsync()
    {
        using var reader = new ExchangeFrameReader([new ExchangeFrame { Data = new DataFrame { Payload = ByteString.CopyFromUtf8("opaque") } }]);
        using var stream = new ExchangeClientStream(reader, new ExchangeFrameWriter(), new RequestHead { Method = "GET", WantsUpgrade = true });
        await Assert.ThrowsAsync<InvalidDataException>(() => stream.ReadAsync(new byte[8], CancellationToken.None).AsTask()).ConfigureAwait(true);
    }

    [Fact]
    public async Task UnrequestedSwitchingProtocolsIsRejectedBeforePresentationAsync()
    {
        using var reader = new ExchangeFrameReader([]);
        var writer = new ExchangeFrameWriter();
        using var stream = new ExchangeClientStream(reader, writer, new RequestHead { Method = "GET" });
        await Assert.ThrowsAsync<InvalidDataException>(() => stream.WriteAsync(AcceptedHead.ToArray(), CancellationToken.None).AsTask()).ConfigureAwait(true);
        Assert.Empty(writer.Frames);
    }

    [Fact]
    public async Task RejectedUpgradeKeepsHttpResponseAndStopsWaitingUploadAsync()
    {
        using var reader = new ExchangeFrameReader([new ExchangeFrame { UploadStopped = new UploadStopped() }]);
        var writer = new ExchangeFrameWriter();
        using var stream = new ExchangeClientStream(reader, writer, new RequestHead { Method = "GET", WantsUpgrade = true });
        await stream.WriteAsync("HTTP/1.1 403 Forbidden\r\nContent-Length: 0\r\n\r\n"u8.ToArray(), CancellationToken.None).ConfigureAwait(true);
        await stream.CompleteAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.NotNull(writer.Frames[0].StopUpload);
        Assert.Equal(403u, Assert.IsType<ResponseHead>(writer.Frames[1].Response).StatusCode);
        Assert.False(Assert.IsType<ResponseHead>(writer.Frames[1].Response).Upgrade);
        Assert.NotNull(writer.Frames[^1].Complete);
    }

    [Fact]
    public async Task AcceptedUpgradeRequiresTheOpaqueByteDigestAsync()
    {
        using var reader = new ExchangeFrameReader([
            new ExchangeFrame { Data = new DataFrame { Payload = ByteString.CopyFromUtf8("opaque") } },
            new ExchangeFrame { Complete = new Completion { BodyBytes = 6, Sha256 = ByteString.CopyFrom(new byte[32]) } }]);
        using var stream = new ExchangeClientStream(reader, new ExchangeFrameWriter(), new RequestHead { Method = "GET", WantsUpgrade = true });
        await stream.WriteAsync(AcceptedHead.ToArray(), CancellationToken.None).ConfigureAwait(true);
        await stream.ReadExactlyAsync(new byte[6], CancellationToken.None).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(() => stream.ReadAsync(new byte[1], CancellationToken.None).AsTask()).ConfigureAwait(true);
    }

    [Fact]
    public async Task AcceptedUpgradeRejectsHttpTrailersAsync()
    {
        using var reader = new ExchangeFrameReader([new ExchangeFrame { Trailers = new TrailerFrame() }]);
        using var stream = new ExchangeClientStream(reader, new ExchangeFrameWriter(), new RequestHead { Method = "GET", WantsUpgrade = true });
        await stream.WriteAsync(AcceptedHead.ToArray(), CancellationToken.None).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(() => stream.ReadAsync(new byte[1], CancellationToken.None).AsTask()).ConfigureAwait(true);
    }
}

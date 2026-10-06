using Google.Protobuf;
using Mk8.Drava.Transport.Protocol;
using Mk8.Drava.Transport.Protocol.V1;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class RelayStreamingTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    public async Task DuplexBodiesLargerThanTheWindowPreserveBytesAndVerifiedEofAsync(int window)
    {
        var pair = new RelayStreamPair(window: window);
        await using var lifetime = pair.ConfigureAwait(true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var upload = System.Security.Cryptography.RandomNumberGenerator.GetBytes(2 * 1024 * 1024);
        var download = System.Security.Cryptography.RandomNumberGenerator.GetBytes(1024 * 1024);
        using var receivedUpload = new MemoryStream();
        using var receivedDownload = new MemoryStream();
        async Task WriteClientAsync() { await pair.Client.WriteAsync(upload, timeout.Token).ConfigureAwait(true); await pair.Client.CompleteWritesAsync(timeout.Token).ConfigureAwait(true); }
        async Task WriteServerAsync() { await pair.Server.WriteAsync(download, timeout.Token).ConfigureAwait(true); await pair.Server.CompleteWritesAsync(timeout.Token).ConfigureAwait(true); }
        await Task.WhenAll(WriteClientAsync(), WriteServerAsync(), pair.Server.CopyToAsync(receivedUpload, timeout.Token), pair.Client.CopyToAsync(receivedDownload, timeout.Token)).ConfigureAwait(true);
        Assert.Equal(upload, receivedUpload.ToArray()); Assert.Equal(download, receivedDownload.ToArray());
        await Task.WhenAll(pair.Client.FinishAsync(timeout.Token), pair.Server.FinishAsync(timeout.Token)).ConfigureAwait(true);
        pair.EndRequests(); pair.EndResponses();
        await Task.WhenAll(pair.Client.WaitForCompletionAsync(timeout.Token), pair.Server.WaitForCompletionAsync(CancellationToken.None)).WaitAsync(timeout.Token).ConfigureAwait(true);
    }

    [Fact]
    public async Task CreditsWaitForAWholeDownstreamFrameToBeConsumedAsync()
    {
        var pair = new RelayStreamPair();
        await using var lifetime = pair.ConfigureAwait(true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var pending = pair.Client.WriteAsync(new byte[5 * FrameLimits.MaximumFrameBytes], timeout.Token).AsTask();
        while (pair.ClientFrames < 4) await Task.Delay(1, timeout.Token).ConfigureAwait(true);
        Assert.False(pending.IsCompleted);
        var buffer = new byte[FrameLimits.MaximumFrameBytes];
        Assert.Equal(1, await pair.Server.ReadAsync(buffer.AsMemory(0, 1), timeout.Token).ConfigureAwait(true));
        Assert.False(pending.IsCompleted);
        Assert.Equal(buffer.Length - 1, await pair.Server.ReadAsync(buffer.AsMemory(1), timeout.Token).ConfigureAwait(true));
        await pending.WaitAsync(timeout.Token).ConfigureAwait(true);
        Assert.Equal(5, pair.ClientFrames);
    }

    [Fact]
    public async Task BareTransportEofAndIncorrectDigestNeverBecomeSuccessfulEofAsync()
    {
        var pair = new RelayStreamPair();
        await using var lifetime = pair.ConfigureAwait(true);
        pair.EndRequests();
        await Assert.ThrowsAsync<EndOfStreamException>(() => pair.Server.WaitForCompletionAsync(CancellationToken.None)).ConfigureAwait(true);
        var malformed = new RelayStreamPair();
        await using var malformedLifetime = malformed.ConfigureAwait(true);
        await malformed.InjectRequestAsync(new RelayFrame { Complete = new Completion { BodyBytes = 0, Sha256 = ByteString.CopyFrom(new byte[32]) } }).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(() => malformed.Server.WaitForCompletionAsync(CancellationToken.None)).ConfigureAwait(true);
    }

    [Fact]
    public async Task UnauthorizedFramesAndOvercreditedPeersAbortAsync()
    {
        var pair = new RelayStreamPair();
        await using var lifetime = pair.ConfigureAwait(true);
        await pair.InjectRequestAsync(new RelayFrame { Consumed = new Consumed { Direction = Consumed.Types.Direction.Response, Frames = 1 } }).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(() => pair.Server.WaitForCompletionAsync(CancellationToken.None)).ConfigureAwait(true);
        var flooding = new RelayStreamPair();
        await using var floodingLifetime = flooding.ConfigureAwait(true);
        for (var index = 0; index < 5; index++) await flooding.InjectRequestAsync(new RelayFrame { Data = new DataFrame { Payload = ByteString.CopyFromUtf8("data") } }).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(() => flooding.Server.WaitForCompletionAsync(CancellationToken.None)).ConfigureAwait(true);
    }

    [Fact]
    public async Task CapabilityByteLimitsAndCancellationBoundOutstandingWritesAsync()
    {
        var pair = new RelayStreamPair(maximumBytes: 10);
        await using var lifetime = pair.ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(() => pair.Client.WriteAsync(new byte[11], CancellationToken.None).AsTask()).ConfigureAwait(true);
        var blocked = new RelayStreamPair();
        await using var blockedLifetime = blocked.ConfigureAwait(true);
        using var cancellation = new CancellationTokenSource();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var write = blocked.Client.WriteAsync(new byte[5 * FrameLimits.MaximumFrameBytes], cancellation.Token).AsTask();
        while (blocked.ClientFrames < 4) await Task.Delay(1, timeout.Token).ConfigureAwait(true);
        await cancellation.CancelAsync().ConfigureAwait(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await write.WaitAsync(timeout.Token).ConfigureAwait(true)).ConfigureAwait(true);
    }
}

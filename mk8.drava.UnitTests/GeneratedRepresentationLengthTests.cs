using Mk8.Drava.Application.BLL.ControlPlane.Routing;
using Mk8.Drava.Application.INF.Proxy.Exchange;
using Mk8.Drava.Transport.Protocol;
using Mk8.Drava.Transport.Protocol.V1;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class GeneratedRepresentationLengthTests
{
    [Theory]
    [InlineData("GET", 204, null, 0ul)]
    [InlineData("GET", 304, null, 0ul)]
    [InlineData("HEAD", 200, 3L, 0ul)]
    [InlineData("GET", 200, 3L, 3ul)]
    public async Task GeneratedResponsesDistinguishRepresentationSizeAndTransmittedBytesAsync(string method, int status, long? expected, ulong bytes)
    {
        using var emptyUpload = new BodyDigest();
        using var reader = new ExchangeFrameReader([new ExchangeFrame { Complete = emptyUpload.Complete() }]);
        var writer = new ExchangeFrameWriter();
        using var stream = new ExchangeClientStream(reader, writer, new RequestHead { Method = method });
        await stream.VerifyEmptyUploadAsync(CancellationToken.None).ConfigureAwait(true);
        // Forwarding dependencies are deliberately unused by GeneratedAsync.
        var executor = new MdravaProxyExchangeExecutor(stream, null!, null!);
        await executor.GeneratedAsync(new GeneratedRouteResponse(status, "Test", "text/plain", "abc", []), "req", CancellationToken.None).ConfigureAwait(true);
        await stream.CompleteAsync(CancellationToken.None).ConfigureAwait(true);
        var head = Assert.IsType<ResponseHead>(writer.Frames[0].Response);
        var lengths = head.Headers.Where(static field => string.Equals(field.Name, "content-length", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (expected is { } length) Assert.Collection(lengths, field => Assert.Equal(length.ToString(System.Globalization.CultureInfo.InvariantCulture), field.Value));
        else Assert.Empty(lengths);
        Assert.Equal(bytes, Assert.IsType<Completion>(writer.Frames[^1].Complete).BodyBytes);
    }
}

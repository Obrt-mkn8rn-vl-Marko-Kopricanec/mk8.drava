using Google.Protobuf;
using Mk8.Drava.Transport.Protocol;
using Mk8.Drava.Transport.Protocol.V1;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class ExchangeContractTests
{
    [Fact]
    public void CompletionRejectsTruncationAndCorruption()
    {
        using var sender = new BodyDigest();
        sender.Append("payload"u8);
        var completion = sender.Complete();
        using var truncated = new BodyDigest();
        truncated.Append("pay"u8);
        Assert.Throws<InvalidDataException>(() => truncated.Verify(completion));
        using var corrupted = new BodyDigest();
        corrupted.Append("payloae"u8);
        Assert.Throws<InvalidDataException>(() => corrupted.Verify(completion));
        using var correct = new BodyDigest();
        correct.Append("pay"u8);
        correct.Append("load"u8);
        correct.Verify(completion);
        Assert.Throws<InvalidDataException>(() => correct.Verify(completion));
    }

    [Fact]
    public void PrivatePeerCannotInjectFramingThroughHeaderValues()
    {
        var request = Request();
        request.Headers.Add(new Header { Name = "x-test", Value = "value\r\nContent-Length: 0" });
        Assert.Throws<InvalidDataException>(() => FrameLimits.ValidateRequest(request));
    }

    [Fact]
    public void PeerCannotUseAbsoluteTargetsOrNegativeLengths()
    {
        var request = Request();
        request.RawTarget = "http://169.254.169.254/metadata";
        Assert.Throws<InvalidDataException>(() => FrameLimits.ValidateRequest(request));
        request.RawTarget = "/";
        request.ContentLength = -1;
        Assert.Throws<InvalidDataException>(() => FrameLimits.ValidateRequest(request));
    }

    [Fact]
    public void BodyFramesAndMetadataHaveIndependentFiniteBounds()
    {
        Assert.Throws<InvalidDataException>(() => FrameLimits.ValidateData(new DataFrame { Payload = ByteString.CopyFrom(new byte[FrameLimits.MaximumFrameBytes + 1]) }));
        var request = Request();
        for (var index = 0; index <= FrameLimits.MaximumHeaderCount; index++) request.Headers.Add(new Header { Name = "x-field", Value = "value" });
        Assert.Throws<InvalidDataException>(() => FrameLimits.ValidateRequest(request));
    }

    [Theory]
    [InlineData("content-length")]
    [InlineData("transfer-encoding")]
    [InlineData("authorization")]
    [InlineData("host")]
    public void TrailersCannotChangeFramingOrAuthority(string name)
    {
        Assert.Throws<InvalidDataException>(() => FrameLimits.ValidateHeaders([new Header { Name = name, Value = "value" }], trailers: true));
    }

    private static RequestHead Request() => new()
    {
        Version = 1,
        ExchangeId = Guid.NewGuid().ToString("N"),
        GatewayId = "local",
        ListenerId = "http",
        Method = "GET",
        RawTarget = "/",
        Authority = "service.drava.test",
        Scheme = "http",
        ClientProtocol = "HTTP/1.1",
        PeerAddress = "127.0.0.1",
    };
}

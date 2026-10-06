using Mk8.Drava.Transport.Protocol;
using Mk8.Drava.Transport.Protocol.V1;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class ExchangeFramingSecurityTests
{
    [Theory]
    [InlineData("Host", "wrong.test")]
    [InlineData("Content-Length", "7")]
    [InlineData("Transfer-Encoding", "chunked")]
    [InlineData("Transfer-Encoding", "gzip")]
    public void RejectsConflictingPresentationFramingFacts(string name, string value)
    {
        var head = new RequestHead { Version = FrameLimits.Version, ExchangeId = Guid.NewGuid().ToString("N"), GatewayId = "local",
            ListenerId = "http", Method = "POST", RawTarget = "/", Authority = "right.test", Scheme = "http", ClientProtocol = "HTTP/1.1",
            PeerAddress = "127.0.0.1", HasBody = true, ContentLength = 3 };
        head.Headers.Add(new Header { Name = name, Value = value });
        Assert.Throws<InvalidDataException>(() => FrameLimits.ValidateRequest(head));
    }

    [Fact]
    public void RejectsDuplicateLengthHeadersAtApplicationBoundary()
    {
        var head = new RequestHead { Version = FrameLimits.Version, ExchangeId = Guid.NewGuid().ToString("N"), GatewayId = "local",
            ListenerId = "http", Method = "POST", RawTarget = "/", Authority = "right.test", Scheme = "http", ClientProtocol = "HTTP/1.1",
            PeerAddress = "127.0.0.1", HasBody = true, ContentLength = 3 };
        head.Headers.Add(new Header { Name = "content-length", Value = "3" });
        head.Headers.Add(new Header { Name = "content-length", Value = "3" });
        Assert.Throws<InvalidDataException>(() => FrameLimits.ValidateRequest(head));
    }
}

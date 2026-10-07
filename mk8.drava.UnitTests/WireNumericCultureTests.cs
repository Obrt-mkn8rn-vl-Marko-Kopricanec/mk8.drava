using System.Buffers.Binary;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Caching;
using Mk8.Drava.Application.BLL.ControlPlane.Http1;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.INF.Proxy.Forwarding;
using Mk8.Drava.Application.INF.Proxy.Http2;
using Mk8.Drava.Application.INF.Proxy.RuntimeGuards;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class WireNumericCultureTests
{
    [Theory]
    [InlineData("200", true)]
    [InlineData("+200", false)]
    [InlineData("0200", false)]
    [InlineData(" 200", false)]
    [InlineData("200 ", false)]
    [InlineData("P200", false)]
    [InlineData("M200", false)]
    public async Task UpstreamStatusRequiresThreeAsciiDigitsRegardlessOfLocaleAsync(string value, bool accepted)
    {
        using var culture = new WireCultureScope();
        using var stream = StatusFrame(value);
        var connection = new Http2UpstreamConnection(stream, new ProxyMetrics());
        await using var connectionLifetime = connection.ConfigureAwait(true);
        var timeouts = RuntimeTimeoutsFactory.ForHealthCheck(TimeSpan.FromSeconds(1));
        if (!accepted)
        {
            await Assert.ThrowsAsync<Http2UpstreamProtocolException>(async () =>
                await connection.ReadResponseHeadAsync(4096, timeouts, CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
            return;
        }
        var head = await connection.ReadResponseHeadAsync(4096, timeouts, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(200, head.StatusCode);
        Assert.True(head.EndStream);
    }

    [Theory]
    [InlineData("127.0.0.0/24", true)]
    [InlineData("127.0.0.0/+24", false)]
    [InlineData("127.0.0.0/P24", false)]
    [InlineData("127.0.0.0/M24", false)]
    public void TrustedProxyPrefixesDoNotUseLocaleSignCharacters(string entry, bool trusted)
    {
        using var culture = new WireCultureScope();
        var policy = new ProxyForwardedHeadersAddressPolicy();
        Assert.Equal(trusted, policy.IsValidEntry(entry));
        Assert.Equal(trusted, policy.IsTrustedPeer("127.0.0.1", [entry]));
    }

    [Theory]
    [InlineData("10", 10)]
    [InlineData("+10", 60)]
    [InlineData("P10", 60)]
    public void CacheAgeUsesInvariantValuesAndRetainsInvalidValueFallback(string value, int seconds)
    {
        using var culture = new WireCultureScope();
        var policy = new ProxyCachePolicyFacts(true, 1024, 4096, TimeSpan.FromSeconds(60), true, [], [200], ["GET"]);
        var response = new Http1ResponseHead("HTTP/1.1", 200, "OK", Http1ResponseFraming.FromContentLength(0),
            [new ProxyHeaderField("Cache-Control", "max-age=" + value)]);
        var result = Assert.IsType<ProxyCacheStorageEligibilityResult.AcceptedResult>(ProxyCacheEligibilityPolicy.EvaluateStoredResponse(policy, response, 0));
        Assert.Equal(TimeSpan.FromSeconds(seconds), result.Ttl);
    }

    [Theory]
    [InlineData("10", 10L)]
    [InlineData("+10", null)]
    [InlineData("P10", null)]
    public void RouteDiagnosticsInterpretLengthsWithoutLocaleSigns(string value, long? length)
    {
        using var culture = new WireCultureScope();
        var request = new RouteMatchDryRunRequest("http", "example.test", null, "POST", "/", "",
            new Dictionary<string, string?>(StringComparer.Ordinal) { ["Content-Length"] = value }, null, null);
        var decision = Assert.IsType<ProxyRouteDiagnosticsRequestDecision.AcceptedDecision>(
            ProxyRouteDiagnosticsRequestReader.Read(request, DateTimeOffset.UnixEpoch, new ProxyClientAddressSyntaxPolicy()));
        Assert.Equal(length, decision.Input.RequestHead.Framing.ContentLength);
    }

    private static MemoryStream StatusFrame(string status)
    {
        var payload = HpackCodec.EncodeRequestHeaders([new ProxyHeaderField(":status", status)]);
        var frame = new byte[9 + payload.Length];
        frame[0] = (byte)(payload.Length >> 16);
        frame[1] = (byte)(payload.Length >> 8);
        frame[2] = (byte)payload.Length;
        frame[3] = 1;
        frame[4] = 5;
        BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(5, 4), 1);
        payload.CopyTo(frame, 9);
        return new MemoryStream(frame, writable: false);
    }
}

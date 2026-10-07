using System.Text;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.INF.Proxy.Http2;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class HpackResponseBoundsTests
{
    [Theory]
    [InlineData("ff")]
    [InlineData("3f")]
    [InlineData("2188")]
    [InlineData("8820")]
    [InlineData("20202088")]
    [InlineData("4001780179be")]
    // This integer wraps to static index 8 in the inherited unchecked decoder.
    [InlineData("ff89ffffff0f")]
    [InlineData("0001787f82ffffff0f79")]
    [InlineData("0001787f80808080800079")]
    public void InvalidIntegersAndTableStateFailWithoutPartialHeaders(string hex)
    {
        Assert.False(HpackCodec.TryDecodeResponseHeaders(Convert.FromHexString(hex), 4096, 128, out var fields, out _));
        Assert.Empty(fields);
    }

    [Theory]
    [InlineData("88")]
    [InlineData("2088")]
    [InlineData("202088")]
    [InlineData("884001780179")]
    public void StaticAndLiteralFieldsRemainLegalWithAZeroTable(string hex)
    {
        Assert.True(HpackCodec.TryDecodeResponseHeaders(Convert.FromHexString(hex), 4096, 128, out var fields, out var reason), reason);
        Assert.Equal("200", fields[0].Value);
        if (fields.Count > 1) Assert.Equal("y", fields[1].Value);
    }

    [Fact]
    public void DecodedByteAndFieldLimitsAreIndependentOfEncodedSize()
    {
        var fields = new byte[129]; fields[0] = 0x88; fields.AsSpan(1).Fill(0x90);
        Assert.False(HpackCodec.TryDecodeResponseHeaders(fields, 4096, 128, out _, out _));
        Assert.True(HpackCodec.TryDecodeResponseHeaders(fields[..128], 4096, 128, out var boundary, out _));
        Assert.Equal(128, boundary.Count);
        Assert.False(HpackCodec.TryDecodeResponseHeaders(fields[..17], 256, 128, out _, out _));
        Assert.True(HpackCodec.TryDecodeResponseHeaders([0x88], 10, 1, out _, out _));
        Assert.False(HpackCodec.TryDecodeResponseHeaders([0x88], 9, 1, out _, out _));
    }

    [Theory]
    [InlineData("X", "value")]
    [InlineData("x", " value")]
    [InlineData("x", "value\t")]
    [InlineData("x", "a\0b")]
    [InlineData("x:y", "value")]
    [InlineData("\u00ff", "value")]
    [InlineData("x", "a\rb")]
    [InlineData("x", "a\nb")]
    public async Task InvalidFieldOctetsAreRejectedBeforeAResponseHeadAsync(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        using var pair = await Http2TestConnectionPair.CreateAsync().ConfigureAwait(true);
        var connection = new Http2UpstreamConnection(pair.Client, new ProxyMetrics());
        await using var lifetime = connection.ConfigureAwait(true);
        await pair.InitializeAsync(connection).ConfigureAwait(true);
        await pair.RequestAsync(connection).ConfigureAwait(true);
        var block = Literal(name, value, withStatus: true);
        await Http2TestFrames.WriteAsync(pair.Server, Http2TestFrameType.Headers, 5, 1, block, pair.Deadline.Token).ConfigureAwait(true);
        await Assert.ThrowsAsync<Http2UpstreamProtocolException>(async () =>
            await connection.ReadResponseHeadAsync(4096, RuntimeTimeoutsFactory.ForHealthCheck(TimeSpan.FromSeconds(5)), pair.Deadline.Token).ConfigureAwait(true)).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(128, 4096)]
    [InlineData(16, 256)]
    public async Task ExpandedInitialFieldsAreBoundedDuringDecodingAsync(int repeatedFields, int byteLimit)
    {
        using var pair = await Http2TestConnectionPair.CreateAsync().ConfigureAwait(true);
        var connection = new Http2UpstreamConnection(pair.Client, new ProxyMetrics());
        await using var lifetime = connection.ConfigureAwait(true);
        await pair.InitializeAsync(connection).ConfigureAwait(true);
        await pair.RequestAsync(connection).ConfigureAwait(true);
        var block = new byte[repeatedFields + 1]; block[0] = 0x88; block.AsSpan(1).Fill(0x90);
        Assert.True(block.Length < byteLimit);
        await Http2TestFrames.WriteAsync(pair.Server, Http2TestFrameType.Headers, 5, 1, block, pair.Deadline.Token).ConfigureAwait(true);
        await Assert.ThrowsAsync<Http2UpstreamProtocolException>(async () =>
            await connection.ReadResponseHeadAsync(byteLimit, RuntimeTimeoutsFactory.ForHealthCheck(TimeSpan.FromSeconds(5)), pair.Deadline.Token).ConfigureAwait(true)).ConfigureAwait(true);
    }

    [Theory]
    [InlineData("X", "value")]
    [InlineData("x", " value")]
    [InlineData("x", "value\t")]
    [InlineData("\u00ff", "value")]
    public async Task TrailersUseTheSameFieldValidityRulesAsync(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        using var pair = await Http2TestConnectionPair.CreateAsync().ConfigureAwait(true);
        var connection = new Http2UpstreamConnection(pair.Client, new ProxyMetrics());
        await using var lifetime = connection.ConfigureAwait(true);
        await pair.InitializeAsync(connection).ConfigureAwait(true);
        await pair.RequestAsync(connection).ConfigureAwait(true);
        var timeouts = RuntimeTimeoutsFactory.ForHealthCheck(TimeSpan.FromSeconds(5));
        await Http2TestFrames.WriteAsync(pair.Server, Http2TestFrameType.Headers, 4, 1, new byte[] { 0x88 }, pair.Deadline.Token).ConfigureAwait(true);
        await connection.ReadResponseHeadAsync(4096, timeouts, pair.Deadline.Token).ConfigureAwait(true);
        await Http2TestFrames.WriteAsync(pair.Server, Http2TestFrameType.Headers, 5, 1, Literal(name, value, withStatus: false), pair.Deadline.Token).ConfigureAwait(true);
        await Assert.ThrowsAsync<Http2UpstreamProtocolException>(async () =>
            await connection.ReadDataAsync(timeouts, pair.Deadline.Token).ConfigureAwait(true)).ConfigureAwait(true);
    }

    private static byte[] Literal(string name, string value, bool withStatus)
    {
        byte[] prefix = withStatus ? [0x88] : [];
        return [.. prefix, 0, (byte)name.Length, .. Encoding.Latin1.GetBytes(name), (byte)value.Length, .. Encoding.Latin1.GetBytes(value)];
    }
}

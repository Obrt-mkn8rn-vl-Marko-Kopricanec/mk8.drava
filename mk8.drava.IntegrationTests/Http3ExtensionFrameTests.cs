using System.Net.Quic;
using System.Runtime.Versioning;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.INF.Proxy.Http3;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("osx")]
public sealed class Http3ExtensionFrameTests
{
    [Theory]
    [InlineData("before")]
    [InlineData("interim")]
    [InlineData("body")]
    [InlineData("trailers")]
    public async Task ActualQuicIgnoresOpaqueExtensionFramesWithoutWrappingTheir62BitTypesAsync(string position)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var fixture = await DevelopmentOwnedQuicPeer.CreateAsync(processor: null, deadline.Token).ConfigureAwait(true);
        await using var fixtureLifetime = fixture.ConfigureAwait(true);
        Assert.True(fixture.Connection.TryReserveStream(TimeSpan.FromMinutes(1)));
        var timeouts = RuntimeTimeoutsFactory.ForHealthCheck(TimeSpan.FromSeconds(5));
        var client = await Http3UpstreamConnection.OpenStreamAsync(fixture.Connection, timeouts, fixture.Metrics, 16384, deadline.Token).ConfigureAwait(true);
        await using var clientLifetime = client.ConfigureAwait(true);
        var peer = RespondAsync(fixture, position, deadline.Token);
        try
        {
            await client.SendHeadersAsync([new(":method", "GET"), new(":scheme", "https"), new(":authority", "upstream.test"), new(":path", "/extensions")], true, timeouts, deadline.Token).ConfigureAwait(true);
            Assert.Equal(200, (await client.ReadResponseHeadAsync(1024, timeouts, deadline.Token).ConfigureAwait(true)).StatusCode);
            Assert.Equal("abc"u8.ToArray(), (await client.ReadDataAsync(timeouts, deadline.Token).ConfigureAwait(true)).Data);
            var end = await client.ReadDataAsync(timeouts, deadline.Token).ConfigureAwait(true);
            Assert.True(end.EndStream);
            Assert.Collection(Assert.IsAssignableFrom<IReadOnlyList<ProxyHeaderField>>(end.Trailers),
                static field => Assert.Equal(new ProxyHeaderField("x-end", "done"), field));
            await peer.ConfigureAwait(true);
        }
        finally
        {
            await deadline.CancelAsync().ConfigureAwait(true);
            try { await peer.ConfigureAwait(true); }
            catch (Exception exception) when (exception is OperationCanceledException or IOException or QuicException) { }
        }
    }

    private static async Task RespondAsync(DevelopmentOwnedQuicPeer fixture, string position, CancellationToken cancellationToken)
    {
        var stream = await fixture.AcceptRequestAsync(cancellationToken).ConfigureAwait(false);
        await using var streamLifetime = stream.ConfigureAwait(false);
        using var request = new MemoryStream();
        await stream.CopyToAsync(request, cancellationToken).ConfigureAwait(false);
        using var response = new MemoryStream();
        AddExtension(response, position, "before");
        Http3Codec.WriteFrame(response, Http3Codec.HeadersFrame, Http3Codec.EncodeHeaderBlock([new(":status", "103")]));
        AddExtension(response, position, "interim");
        Http3Codec.WriteFrame(response, Http3Codec.HeadersFrame, Http3Codec.EncodeHeaderBlock([new(":status", "200"), new("content-length", "3")]));
        Http3Codec.WriteFrame(response, Http3Codec.DataFrame, "abc"u8);
        AddExtension(response, position, "body");
        Http3Codec.WriteFrame(response, Http3Codec.HeadersFrame, Http3Codec.EncodeHeaderBlock([new("x-end", "done")]));
        AddExtension(response, position, "trailers");
        await stream.WriteAsync(response.ToArray(), completeWrites: true, cancellationToken).ConfigureAwait(false);
    }

    private static void AddExtension(Stream stream, string selected, string position)
    {
        if (!string.Equals(selected, position, StringComparison.Ordinal)) return;
        Http3Codec.WriteFrame(stream, 0x100000000, "opaque"u8);
        Http3Codec.WriteFrame(stream, 0x3fffffffffffffff, ReadOnlySpan<byte>.Empty);
    }
}

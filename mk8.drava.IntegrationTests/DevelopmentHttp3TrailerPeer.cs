using System.Globalization;
using System.Net.Quic;
using System.Runtime.Versioning;
using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.INF.Proxy.Http3;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("osx")]
internal sealed class DevelopmentHttp3TrailerPeer(DevelopmentPendingQuicPeer listener)
{
    private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void FinishResponse() => _finished.TrySetResult();

    public async Task RespondAsync(int requests, bool requestTrailers, CancellationToken cancellationToken)
    {
        var connection = await listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
        await using var connectionLifetime = connection.ConfigureAwait(false);
        List<QuicStream> controls = [];
        try
        {
            for (var index = 1; index <= requests; index++)
            {
                var stream = await AcceptRequestAsync(connection, controls, cancellationToken).ConfigureAwait(false);
                await using var streamLifetime = stream.ConfigureAwait(false);
                await RespondToRequestAsync(stream, index, requestTrailers, cancellationToken).ConfigureAwait(false);
            }
            await _finished.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            for (var index = 0; index < controls.Count; index++) await controls[index].DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task<QuicStream> AcceptRequestAsync(QuicConnection connection, List<QuicStream> controls,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var stream = await connection.AcceptInboundStreamAsync(cancellationToken).ConfigureAwait(false);
            if (stream.Type == QuicStreamType.Bidirectional) return stream;
            controls.Add(stream);
        }
    }

    private static async Task RespondToRequestAsync(QuicStream stream, int index, bool requestTrailers,
        CancellationToken cancellationToken)
    {
        using var request = new MemoryStream();
        await stream.CopyToAsync(request, cancellationToken).ConfigureAwait(false);
        VerifyRequest(request.ToArray(), requestTrailers);
        using var response = new MemoryStream();
        Http3Codec.WriteFrame(response, Http3Codec.HeadersFrame, Http3Codec.EncodeHeaderBlock([
            new(":status", "200"), new("content-length", "3"), new("content-type", "application/grpc"), new("cache-control", "max-age=60")]));
        Http3Codec.WriteFrame(response, Http3Codec.DataFrame, "abc"u8);
        Http3Codec.WriteFrame(response, Http3Codec.HeadersFrame, Http3Codec.EncodeHeaderBlock([
            new("grpc-status", "0"), new("grpc-message", "complete%20here"), new("x-final-status", index.ToString(CultureInfo.InvariantCulture))]));
        await stream.WriteAsync(response.ToArray(), completeWrites: true, cancellationToken).ConfigureAwait(false);
    }

    private static void VerifyRequest(byte[] request, bool trailersExpected)
    {
        var offset = 0;
        Assert.True(Http3Codec.TryReadFrame(request, ref offset, out var type, out var payload));
        Assert.Equal(Http3Codec.HeadersFrame, type);
        Assert.True(Http3Codec.TryDecodeHeaderBlock(payload.Span, 4096, out var head, out var reason), reason);
        Assert.Contains(head, field => field.Name.Equals(":path", StringComparison.Ordinal)
            && field.Value.Equals(trailersExpected ? "/echo" : "/cached", StringComparison.Ordinal));
        using var body = new MemoryStream();
        IReadOnlyList<ProxyHeaderField>? trailers = null;
        while (Http3Codec.TryReadFrame(request, ref offset, out type, out payload))
        {
            if (type == Http3Codec.DataFrame) { Assert.Null(trailers); body.Write(payload.Span); }
            else
            {
                Assert.Equal(Http3Codec.HeadersFrame, type);
                Assert.Null(trailers);
                Assert.True(Http3Codec.TryDecodeHeaderBlock(payload.Span, 4096, out trailers, out reason), reason);
            }
        }
        Assert.Equal(request.Length, offset);
        if (trailersExpected)
        {
            Assert.Equal("abc"u8.ToArray(), body.ToArray());
            Assert.Collection(Assert.IsAssignableFrom<IReadOnlyList<ProxyHeaderField>>(trailers),
                static field => Assert.Equal(new ProxyHeaderField("x-client-end", "client-complete"), field));
        }
        else { Assert.Empty(body.ToArray()); Assert.Null(trailers); }
    }
}

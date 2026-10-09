using System.Net.Quic;
using System.Runtime.Versioning;
using Mk8.Drava.Application.INF.Proxy.Http3;
using Xunit;

namespace Mk8.Drava.IntegrationTests;

[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("osx")]
internal sealed class DevelopmentHttp3Peer
{
    private readonly DevelopmentPendingQuicPeer _listener;
    private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public DevelopmentHttp3Peer(DevelopmentPendingQuicPeer listener) => _listener = listener;
    public int Port => _listener.Port;
    public void FinishResponse() => _finished.TrySetResult();

    public async Task RespondInformationalAsync(CancellationToken cancellationToken)
    {
        var connection = await _listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
        await using var connectionLifetime = connection.ConfigureAwait(false);
        List<QuicStream> controls = [];
        try
        {
            while (true)
            {
                var stream = await connection.AcceptInboundStreamAsync(cancellationToken).ConfigureAwait(false);
                if (stream.Type == QuicStreamType.Bidirectional)
                {
                    await using var streamLifetime = stream.ConfigureAwait(false);
                    await RespondAsync(stream, cancellationToken).ConfigureAwait(false);
                    await _finished.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    return;
                }
                controls.Add(stream);
            }
        }
        finally
        {
            for (var index = 0; index < controls.Count; index++) await controls[index].DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task RespondAsync(QuicStream stream, CancellationToken cancellationToken)
    {
        using var request = new MemoryStream();
        await stream.CopyToAsync(request, cancellationToken).ConfigureAwait(false);
        var offset = 0;
        Assert.True(Http3Codec.TryReadFrame(request.ToArray(), ref offset, out var type, out var payload));
        Assert.Equal(Http3Codec.HeadersFrame, type);
        Assert.True(Http3Codec.TryDecodeHeaderBlock(payload.Span, 4096, out var fields, out var reason), reason);
        Assert.Contains(fields, static field => field.Name.Equals(":path", StringComparison.Ordinal) && field.Value.Equals("/early", StringComparison.Ordinal));
        using var response = new MemoryStream();
        for (var status = 102; status <= 103; status++)
            Http3Codec.WriteFrame(response, Http3Codec.HeadersFrame,
                Http3Codec.EncodeHeaderBlock([new(":status", status.ToString(System.Globalization.CultureInfo.InvariantCulture)), new("link", "</style.css>; rel=preload")]));
        Http3Codec.WriteFrame(response, Http3Codec.HeadersFrame,
            Http3Codec.EncodeHeaderBlock([new(":status", "200"), new("content-length", "5")]));
        Http3Codec.WriteFrame(response, Http3Codec.DataFrame, "final"u8);
        await stream.WriteAsync(response.ToArray(), completeWrites: true, cancellationToken).ConfigureAwait(false);
    }

}

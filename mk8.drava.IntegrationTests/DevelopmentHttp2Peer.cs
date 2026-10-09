using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.INF.Proxy.Http2;

namespace Mk8.Drava.IntegrationTests;

internal sealed class DevelopmentHttp2Peer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly X509Certificate2 _certificate;
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public DevelopmentHttp2Peer(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        _certificate = certificate;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
    }

    public Task RespondAsync(long declaredLength, ReadOnlyMemory<byte> data, bool endedWithHead, bool splitAtDeclaredLength, CancellationToken cancellationToken) =>
        RespondTlsAsync(async (tls, token) =>
    {
        cancellationToken = token;
        var fields = HpackCodec.EncodeResponseHeaders(200, [new ProxyHeaderField("content-length",
            declaredLength.ToString(System.Globalization.CultureInfo.InvariantCulture)), new("cache-control", "max-age=60")]);
        await Http2TestFrames.WriteAsync(tls, Http2TestFrameType.Headers, endedWithHead ? (byte)5 : (byte)4, 1, fields, cancellationToken).ConfigureAwait(false);
        if (!endedWithHead)
        {
            if (splitAtDeclaredLength && declaredLength > 0 && declaredLength < data.Length)
            {
                await Http2TestFrames.WriteAsync(tls, Http2TestFrameType.Data, 0, 1, data[..(int)declaredLength], cancellationToken).ConfigureAwait(false);
                data = data[(int)declaredLength..];
            }
            await Http2TestFrames.WriteAsync(tls, Http2TestFrameType.Data, 1, 1, data, cancellationToken).ConfigureAwait(false);
        }
        await tls.FlushAsync(cancellationToken).ConfigureAwait(false);
    }, cancellationToken);

    public Task RespondHeadAsync(int statusCode, long? contentLength, CancellationToken cancellationToken) =>
        RespondTlsAsync(async (tls, token) =>
        {
            List<ProxyHeaderField> headers = [new("cache-control", "max-age=60")];
            if (contentLength is { } length) headers.Add(new("content-length", length.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            await Http2TestFrames.WriteAsync(tls, Http2TestFrameType.Headers, 5, 1,
                HpackCodec.EncodeResponseHeaders(statusCode, headers), token).ConfigureAwait(false);
            await tls.FlushAsync(token).ConfigureAwait(false);
        }, cancellationToken);

    public Task RespondInformationalAsync(CancellationToken cancellationToken) =>
        RespondTlsAsync(async (tls, token) =>
        {
            for (var status = 102; status <= 103; status++)
                await Http2TestFrames.WriteAsync(tls, Http2TestFrameType.Headers, 4, 1,
                    HpackCodec.EncodeResponseHeaders(status, [new("link", "</style.css>; rel=preload")]), token).ConfigureAwait(false);
            await Http2TestFrames.WriteAsync(tls, Http2TestFrameType.Headers, 4, 1,
                HpackCodec.EncodeResponseHeaders(200, [new("content-length", "5")]), token).ConfigureAwait(false);
            await Http2TestFrames.WriteAsync(tls, Http2TestFrameType.Data, 1, 1, "final"u8.ToArray(), token).ConfigureAwait(false);
            await tls.FlushAsync(token).ConfigureAwait(false);
        }, cancellationToken);

    public Task RespondHeaderBlockAsync(ReadOnlyMemory<byte> block, CancellationToken cancellationToken) =>
        RespondTlsAsync(async (tls, token) =>
        {
            await Http2TestFrames.WriteAsync(tls, Http2TestFrameType.Headers, 5, 1, block, token).ConfigureAwait(false);
            await tls.FlushAsync(token).ConfigureAwait(false);
        }, cancellationToken);

    private async Task RespondTlsAsync(Func<SslStream, CancellationToken, Task> writeResponse, CancellationToken cancellationToken)
    {
        using var socket = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
        var tls = new SslStream(socket.GetStream(), leaveInnerStreamOpen: true);
        await using var tlsLifetime = tls.ConfigureAwait(false);
        await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = _certificate,
            ApplicationProtocols = [SslApplicationProtocol.Http2] }, cancellationToken).ConfigureAwait(false);
        if (tls.NegotiatedApplicationProtocol != SslApplicationProtocol.Http2) throw new InvalidDataException("Test peer requires HTTP/2 ALPN.");
        var preface = await Http2TestFrames.ReadExactAsync(tls, 24, cancellationToken).ConfigureAwait(false);
        if (!preface.AsSpan().SequenceEqual("PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8)) throw new InvalidDataException("Test peer requires HTTP/2 preface.");
        await Http2TestFrames.WriteAsync(tls, Http2TestFrameType.Settings, 0, 0, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
        await ReadRequestHeadAsync(tls, cancellationToken).ConfigureAwait(false);
        await writeResponse(tls, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ReadRequestHeadAsync(Stream stream, CancellationToken cancellationToken)
    {
        while (true)
        {
            var frame = await Http2TestFrames.ReadAsync(stream, cancellationToken).ConfigureAwait(false);
            if (frame.Type == Http2TestFrameType.Settings && frame.Flags == 0)
                await Http2TestFrames.WriteAsync(stream, Http2TestFrameType.Settings, 1, 0, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
            if (frame.StreamId == 1 && frame.Type is Http2TestFrameType.Headers or Http2TestFrameType.Continuation && (frame.Flags & 4) != 0) return;
        }
    }

    public void Dispose() => _listener.Dispose();
}

using System.Security.Cryptography;
using System.Runtime.Versioning;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.INF.Proxy.Http3;

namespace Mk8.Drava.IntegrationTests;

[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("osx")]
internal sealed class DevelopmentOwnedQuicPeer : IAsyncDisposable
{
    private static readonly byte[] ControlType = [0];
    private static readonly byte[] GoAway = [4, 0, 7, 1, 0];
    private readonly X509Certificate2 _certificate;
    private readonly QuicListener _listener;
    private readonly QuicConnection _peer;

    private DevelopmentOwnedQuicPeer(X509Certificate2 certificate, QuicListener listener, QuicConnection peer, Http3UpstreamPooledConnection connection, ProxyMetrics metrics)
    {
        _certificate = certificate; _listener = listener; _peer = peer; Connection = connection; Metrics = metrics;
    }

    public Http3UpstreamPooledConnection Connection { get; }
    public ProxyMetrics Metrics { get; }

    public static async Task<DevelopmentOwnedQuicPeer> CreateAsync(Func<QuicStream, CancellationToken, Task>? processor, CancellationToken cancellationToken)
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
            throw new PlatformNotSupportedException("This development ownership proof requires native QUIC support.");
        var certificate = DevelopmentUpstreamCertificate.Create();
        QuicListener? listener = null; QuicConnection? client = null; QuicConnection? peer = null; QuicStream? control = null;
        try
        {
            listener = await OpenListenerAsync(certificate, cancellationToken).ConfigureAwait(false);
            var accepting = listener.AcceptConnectionAsync(cancellationToken).AsTask();
            try
            {
                client = await OpenClientAsync(listener.LocalEndPoint, certificate, cancellationToken).ConfigureAwait(false);
                peer = await accepting.ConfigureAwait(false);
            }
            finally
            {
                if (peer is null)
                {
                    await listener.DisposeAsync().ConfigureAwait(false);
                    try { var unassigned = await accepting.ConfigureAwait(false); await unassigned.DisposeAsync().ConfigureAwait(false); }
                    catch (Exception exception) when (exception is OperationCanceledException or QuicException or ObjectDisposedException) { }
                }
            }
            control = await client.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, cancellationToken).ConfigureAwait(false);
            var metrics = new ProxyMetrics(); metrics.UpstreamHttp3ConnectionOpened(); metrics.UpstreamHttp3PoolConnectionOpened();
            var connection = new Http3UpstreamPooledConnection("owned-peer", new Http3UpstreamTransport(client, control), metrics, TimeProvider.System, 8, processor);
            return new DevelopmentOwnedQuicPeer(certificate, listener, peer, connection, metrics);
        }
        catch
        {
            await DisposePartialAsync(certificate, listener, peer, client, control).ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask<QuicStream> OpenHeldControlAsync(CancellationToken cancellationToken)
    {
        var stream = await _peer.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, cancellationToken).ConfigureAwait(false);
        try { await stream.WriteAsync(ControlType, cancellationToken).ConfigureAwait(false); return stream; }
        catch { await stream.DisposeAsync().ConfigureAwait(false); throw; }
    }

    public static ValueTask SendGoAwayAsync(QuicStream stream, CancellationToken cancellationToken) => stream.WriteAsync(GoAway, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        try { await Connection.DisposeAsync().ConfigureAwait(false); }
        finally
        {
            try { await _peer.DisposeAsync().ConfigureAwait(false); }
            finally { try { await _listener.DisposeAsync().ConfigureAwait(false); } finally { _certificate.Dispose(); } }
        }
    }

    private static async Task DisposePartialAsync(X509Certificate2 certificate, QuicListener? listener, QuicConnection? peer, QuicConnection? client, QuicStream? control)
    {
        try { if (control is not null) await control.DisposeAsync().ConfigureAwait(false); }
        finally
        {
            try { if (client is not null) await client.DisposeAsync().ConfigureAwait(false); }
            finally
            {
                try { if (peer is not null) await peer.DisposeAsync().ConfigureAwait(false); }
                finally { try { if (listener is not null) await listener.DisposeAsync().ConfigureAwait(false); } finally { certificate.Dispose(); } }
            }
        }
    }

    private static ValueTask<QuicListener> OpenListenerAsync(X509Certificate2 certificate, CancellationToken cancellationToken) =>
        QuicListener.ListenAsync(new QuicListenerOptions
        {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0), ApplicationProtocols = [new SslApplicationProtocol("h3")],
            ConnectionOptionsCallback = (_, _, _) => ValueTask.FromResult(new QuicServerConnectionOptions
            {
                ServerAuthenticationOptions = new SslServerAuthenticationOptions { ServerCertificate = certificate, ApplicationProtocols = [new SslApplicationProtocol("h3")] },
                MaxInboundUnidirectionalStreams = 4, MaxInboundBidirectionalStreams = 4, DefaultCloseErrorCode = 0x100, DefaultStreamErrorCode = 0x100,
            }),
        }, cancellationToken);

    private static ValueTask<QuicConnection> OpenClientAsync(IPEndPoint endpoint, X509Certificate2 expected, CancellationToken cancellationToken) =>
        QuicConnection.ConnectAsync(new QuicClientConnectionOptions
        {
            RemoteEndPoint = endpoint, ClientAuthenticationOptions = new SslClientAuthenticationOptions
            {
                TargetHost = "upstream.test", ApplicationProtocols = [new SslApplicationProtocol("h3")], RemoteCertificateValidationCallback = (_, presented, _, errors) =>
                    presented is not null && errors is SslPolicyErrors.None or SslPolicyErrors.RemoteCertificateChainErrors &&
                    CryptographicOperations.FixedTimeEquals(presented.GetRawCertData(), expected.RawData),
            },
            MaxInboundUnidirectionalStreams = 4, MaxInboundBidirectionalStreams = 4, DefaultCloseErrorCode = 0x100, DefaultStreamErrorCode = 0x100,
        }, cancellationToken);
}

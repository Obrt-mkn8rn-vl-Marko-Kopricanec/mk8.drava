using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;

namespace Mk8.Drava.IntegrationTests;

[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("osx")]
internal sealed class DevelopmentPendingQuicPeer : IAsyncDisposable
{
    private readonly X509Certificate2 _certificate;
    private readonly QuicListener _listener;
    private readonly TaskCompletionSource _entered;
    private readonly TaskCompletionSource _release;

    private DevelopmentPendingQuicPeer(X509Certificate2 certificate, QuicListener listener, TaskCompletionSource entered, TaskCompletionSource release)
    {
        _certificate = certificate; _listener = listener; _entered = entered; _release = release;
    }

    public int Port => _listener.LocalEndPoint.Port;
    public Task HandshakeStarted => _entered.Task;
    public void ReleaseHandshake() => _release.TrySetResult();
    public ValueTask<QuicConnection> AcceptAsync(CancellationToken cancellationToken) => _listener.AcceptConnectionAsync(cancellationToken);
    public ValueTask StopListenerAsync() => _listener.DisposeAsync();

    public static async Task<DevelopmentPendingQuicPeer> CreateAsync(CancellationToken cancellationToken)
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
            throw new PlatformNotSupportedException("This pending-open shutdown proof requires native QUIC support.");
        var certificate = DevelopmentUpstreamCertificate.Create();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var listener = await QuicListener.ListenAsync(new QuicListenerOptions
            {
                ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0), ApplicationProtocols = [new SslApplicationProtocol("h3")],
                ConnectionOptionsCallback = async (_, _, token) =>
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(token).ConfigureAwait(false);
                    return new QuicServerConnectionOptions
                    {
                        ServerAuthenticationOptions = new SslServerAuthenticationOptions { ServerCertificate = certificate, ApplicationProtocols = [new SslApplicationProtocol("h3")] },
                        MaxInboundUnidirectionalStreams = 4, MaxInboundBidirectionalStreams = 4, DefaultCloseErrorCode = 0x100, DefaultStreamErrorCode = 0x100,
                    };
                },
            }, cancellationToken).ConfigureAwait(false);
            return new DevelopmentPendingQuicPeer(certificate, listener, entered, release);
        }
        catch { certificate.Dispose(); throw; }
    }

    public async ValueTask DisposeAsync()
    {
        ReleaseHandshake();
        try { await _listener.DisposeAsync().ConfigureAwait(false); }
        finally { _certificate.Dispose(); }
    }
}

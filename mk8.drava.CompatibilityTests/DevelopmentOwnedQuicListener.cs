using System.Net.Quic;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;

namespace Mk8.Drava.CompatibilityTests;

[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("osx")]
internal sealed class DevelopmentOwnedQuicListener : IAsyncDisposable
{
    private readonly QuicListener _listener;
    private readonly X509Certificate2 _certificate;

    public DevelopmentOwnedQuicListener(QuicListener listener, X509Certificate2 certificate)
    {
        _listener = listener;
        _certificate = certificate;
    }

    public ValueTask<QuicConnection> AcceptConnectionAsync(CancellationToken cancellationToken) => _listener.AcceptConnectionAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        try { await _listener.DisposeAsync().ConfigureAwait(false); }
        finally { _certificate.Dispose(); }
    }
}

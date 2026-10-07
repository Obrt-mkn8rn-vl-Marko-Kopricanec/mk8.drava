#pragma warning disable CA1416
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.BLL.ControlPlane.Tls;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Mk8.Drava.CompatibilityTests.LegacyIngress.Proxy.Tls;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests.LegacyIngress.Proxy.Http3;
public sealed class SystemHttp3QuicListenerFactory : IHttp3QuicListenerFactory
{
    private readonly IProxyActiveConfigurationSnapshotReader _configurationStore;
    private readonly ProxyMetrics _metrics;
    private readonly ILogger<SystemHttp3QuicListenerFactory> _logger;
    public SystemHttp3QuicListenerFactory(IProxyActiveConfigurationSnapshotReader configurationStore, ProxyMetrics metrics, ILogger<SystemHttp3QuicListenerFactory> logger)
    {
        _configurationStore = configurationStore;
        _metrics = metrics;
        _logger = logger;
    }

    public bool IsSupported => QuicListener.IsSupported && QuicConnection.IsSupported;

    public async ValueTask<QuicListener> ListenAsync(RuntimeListener listener, ProxyConfigurationSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(listener);
        if (!IsSupported)
        {
            throw new InvalidOperationException("quic_runtime_not_supported");
        }

        var listenAddress = IPAddress.Parse(listener.Address);
        var listenEndPoint = new IPEndPoint(listenAddress, listener.Port);
        var options = new QuicListenerOptions
        {
            ListenEndPoint = listenEndPoint,
            ListenBacklog = listener.Backlog,
            ApplicationProtocols = ListenerProtocolAdvertisement.BuildHttp3Alpn(listener),
            ConnectionOptionsCallback = (_, clientHello, _) =>
            {
                var activeSnapshot = _configurationStore.ReadSnapshot()is ProxyConfigurationSnapshotReadResult.AvailableResult available ? available.Snapshot : snapshot;
                var activeListener = ResolveListener(activeSnapshot, listener);
                var certificate = SelectCertificate(activeSnapshot, activeListener, clientHello.ServerName);
                if (certificate is null)
                {
                    _metrics.TlsNoCertificateForSni();
                    _logger.LogDebug("No QUIC certificate matched SNI host {HostName} for listener {ListenerName}.", clientHello.ServerName ?? "<none>", listener.Name);
                    throw new AuthenticationException("no_certificate");
                }

                return ValueTask.FromResult(new QuicServerConnectionOptions { ServerAuthenticationOptions = new SslServerAuthenticationOptions { EnabledSslProtocols = SslProtocols.Tls13, ClientCertificateRequired = false, CertificateRevocationCheckMode = X509RevocationMode.NoCheck, ApplicationProtocols = ListenerProtocolAdvertisement.BuildHttp3Alpn(activeListener), ServerCertificate = certificate }, MaxInboundBidirectionalStreams = Math.Max(1, activeListener.Http2Limits.MaxConcurrentStreams), MaxInboundUnidirectionalStreams = 8, IdleTimeout = activeSnapshot.Timeouts.ClientKeepAliveIdleTimeout, HandshakeTimeout = activeSnapshot.Timeouts.TlsHandshakeTimeout, DefaultCloseErrorCode = 0x100, DefaultStreamErrorCode = 0x100 });
            }
        };
        return await QuicListener.ListenAsync(options, cancellationToken).ConfigureAwait(false);
    }

    private static RuntimeListener ResolveListener(ProxyConfigurationSnapshot snapshot, RuntimeListener boundListener)
    {
        return snapshot.Listeners.FirstOrDefault(listener => string.Equals(listener.Name, boundListener.Name, StringComparison.OrdinalIgnoreCase) && string.Equals(listener.Address, boundListener.Address, StringComparison.OrdinalIgnoreCase) && listener.Port == boundListener.Port && listener.Transport == boundListener.Transport) ?? boundListener;
    }

    private static X509Certificate2? SelectCertificate(ProxyConfigurationSnapshot snapshot, RuntimeListener listener, string? hostName)
    {
        return TlsCertificateSelector.SelectCertificate(TlsCertificateSelectionInputMapper.FromSources(snapshot.Certificates, listener.DefaultCertificateId, listener.SniCertificates, hostName));
    }
}

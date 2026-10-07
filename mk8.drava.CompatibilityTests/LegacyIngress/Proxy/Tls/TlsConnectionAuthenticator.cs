using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.BLL.ControlPlane.RuntimeGuards;
using Mk8.Drava.Application.BLL.ControlPlane.Timeouts;
using Mk8.Drava.Application.BLL.ControlPlane.Tls;
using Microsoft.Extensions.Logging;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests.LegacyIngress.Proxy.Tls;
public sealed class TlsConnectionAuthenticator
{
    private readonly ProxyMetrics _metrics;
    private readonly ProxyAdmissionController _admission;
    private readonly ILogger<TlsConnectionAuthenticator> _logger;
    public TlsConnectionAuthenticator(ProxyMetrics metrics, ProxyAdmissionController admission, ILogger<TlsConnectionAuthenticator> logger)
    {
        _metrics = metrics;
        _admission = admission;
        _logger = logger;
    }

    public async ValueTask<TlsAuthenticationResult?> AuthenticateAsync(Stream transportStream, ProxyConfigurationSnapshot snapshot, RuntimeListener listener, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(listener);
        _metrics.TlsHandshakeAttempted();
        var handshakeAdmission = _admission.AcquireTlsHandshake(snapshot.Limits.MaxConcurrentTlsHandshakes);
        if (handshakeAdmission is not ProxyAdmissionDecision.AcceptedResult acceptedHandshake)
        {
            _logger.LogDebug("Rejected TLS handshake for listener {ListenerName} because the concurrent handshake limit is exhausted.", listener.Name);
            return null;
        }

        using var handshakeLease = acceptedHandshake.Lease;
        var sslStream = new SslStream(transportStream, false);
        var options = new SslServerAuthenticationOptions
        {
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            ClientCertificateRequired = false,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            ApplicationProtocols = ListenerProtocolAdvertisement.BuildTcpAlpn(listener.Protocols),
            ServerCertificateSelectionCallback = (_, hostName) => SelectCertificateForHandshake(snapshot, listener, hostName) ?? null!
        };
        try
        {
            await ProxyTimeoutPolicy.RunAsync(async timeoutToken =>
            {
                await sslStream.AuthenticateAsServerAsync(options, timeoutToken).ConfigureAwait(false);
            }, snapshot.Timeouts.TlsHandshakeTimeout, ProxyTimeoutKind.TlsHandshake, cancellationToken).ConfigureAwait(false);
            _metrics.TlsHandshakeSucceeded();
            return TlsAuthenticationResult.Succeeded(sslStream);
        }
        catch (ProxyTimeoutException)
        {
            _metrics.TlsHandshakeTimedOut();
            _logger.LogDebug("TLS handshake timed out for listener {ListenerName}.", listener.Name);
            await sslStream.DisposeAsync().ConfigureAwait(false);
            return null;
        }
        catch (AuthenticationException exception)
        {
            _metrics.TlsHandshakeFailed();
            _logger.LogDebug(exception, "TLS handshake failed for listener {ListenerName}.", listener.Name);
            await sslStream.DisposeAsync().ConfigureAwait(false);
            return null;
        }
        catch (IOException exception)
        {
            _metrics.TlsHandshakeFailed();
            _logger.LogDebug(exception, "TLS handshake ended with I/O failure for listener {ListenerName}.", listener.Name);
            await sslStream.DisposeAsync().ConfigureAwait(false);
            return null;
        }
        catch (Exception exception)
        {
            _metrics.TlsHandshakeFailed();
            _logger.LogError(exception, "TLS handshake failed unexpectedly for listener {ListenerName}.", listener.Name);
            await sslStream.DisposeAsync().ConfigureAwait(false);
            return null;
        }
    }

    private System.Security.Cryptography.X509Certificates.X509Certificate2? SelectCertificateForHandshake(ProxyConfigurationSnapshot snapshot, RuntimeListener listener, string? hostName)
    {
        var certificate = TlsCertificateSelector.SelectCertificate(TlsCertificateSelectionInputMapper.FromSources(snapshot.Certificates, listener.DefaultCertificateId, listener.SniCertificates, hostName));
        if (certificate is null)
        {
            RecordNoCertificate(listener, hostName);
        }

        return certificate;
    }

    private void RecordNoCertificate(RuntimeListener listener, string? hostName)
    {
        _metrics.TlsNoCertificateForSni();
        _logger.LogDebug("No TLS certificate matched SNI host {HostName} for listener {ListenerName}.", hostName ?? "<none>", listener.Name);
    }
}

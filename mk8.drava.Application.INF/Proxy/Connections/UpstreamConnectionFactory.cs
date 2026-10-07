using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Upstreams;
using Mk8.Drava.Application.BLL.ControlPlane.Timeouts;
using Mk8.Drava.Application.INF.NodeRelay;
using Mk8.Drava.Contracts.Relay.V1;

namespace Mk8.Drava.Application.INF.Proxy.Connections;
public sealed class UpstreamConnectionFactory
{
    private readonly RegisteredRelayConnector? _registered;
    public UpstreamConnectionFactory() { }
    public UpstreamConnectionFactory(RegisteredRelayConnector registered) { ArgumentNullException.ThrowIfNull(registered); _registered = registered; }

    public async ValueTask<UpstreamTransportConnection> ConnectAsync(UpstreamTransportEndpoint endpoint, TimeSpan connectTimeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (endpoint.MembershipPartition.Length != 0)
        {
            if (_registered is null) throw new IOException("Registered connections require their current authority adapter.");
            var raw = await _registered.TryConnectAsync(endpoint, RelayPurpose.Exchange, cancellationToken).ConfigureAwait(false);
            if (raw is not null)
            {
                try { return new UpstreamTransportConnection(endpoint, socket: null, await CreateStreamAsync(raw, endpoint, connectTimeout, cancellationToken).ConfigureAwait(false)); }
                catch { await raw.DisposeAsync().ConfigureAwait(false); throw; }
            }
        }
        var addresses = await ProxyTimeoutPolicy.RunAsync(timeoutToken => ResolveAddressesAsync(endpoint, timeoutToken), connectTimeout, ProxyTimeoutKind.UpstreamConnect, cancellationToken).ConfigureAwait(false);
        Exception? lastException = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true
            };
            var transferred = false;
            try
            {
                await ProxyTimeoutPolicy.RunAsync(async timeoutToken =>
                {
                    await socket.ConnectAsync(new IPEndPoint(address, endpoint.Port), timeoutToken).ConfigureAwait(false);
                }, connectTimeout, ProxyTimeoutKind.UpstreamConnect, cancellationToken).ConfigureAwait(false);
                var stream = await CreateStreamAsync(socket, endpoint, connectTimeout, cancellationToken).ConfigureAwait(false);
                var connection = new UpstreamTransportConnection(endpoint, socket, stream);
                transferred = true;
                return connection;
            }
            catch (Exception exception)when (exception is SocketException or IOException or AuthenticationException)
            {
                lastException = exception;
            }
            finally
            {
                if (!transferred) socket.Dispose();
            }
        }

        if (lastException is UpstreamTlsException tlsException)
        {
            throw tlsException;
        }

        throw new IOException($"Unable to connect to upstream '{endpoint.Name}' at {endpoint.Address}:{endpoint.Port}.", lastException);
    }

    private static async ValueTask<Stream> CreateStreamAsync(Socket socket, UpstreamTransportEndpoint endpoint, TimeSpan connectTimeout, CancellationToken cancellationToken)
    {
        var networkStream = new NetworkStream(socket, ownsSocket: false);
        try
        {
            return await CreateStreamAsync(networkStream, endpoint, connectTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await networkStream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async ValueTask<Stream> CreateStreamAsync(Stream networkStream, UpstreamTransportEndpoint endpoint, TimeSpan connectTimeout, CancellationToken cancellationToken)
    {
        if (!string.Equals(endpoint.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            return networkStream;
        }

        var tlsStream = TlsReadBoundary.Create(networkStream, (_, _, _, errors) => errors == SslPolicyErrors.None || !endpoint.ValidateCertificate);
        var targetHost = endpoint.EffectiveSniHost;
        try
        {
            await ProxyTimeoutPolicy.RunAsync(async timeoutToken =>
            {
                await tlsStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = targetHost, EnabledSslProtocols = SslProtocols.None, CertificateRevocationCheckMode = X509RevocationMode.NoCheck, ApplicationProtocols = BuildApplicationProtocols(endpoint) }, timeoutToken).ConfigureAwait(false);
            }, connectTimeout, ProxyTimeoutKind.UpstreamConnect, cancellationToken).ConfigureAwait(false);
            if (RuntimeUpstreamProtocol.IsHttp2(endpoint.Protocol) && tlsStream.NegotiatedApplicationProtocol != SslApplicationProtocol.Http2)
            {
                throw new UpstreamTlsException($"TLS ALPN negotiation for upstream '{endpoint.Name}' selected '{FormatNegotiatedProtocol(tlsStream.NegotiatedApplicationProtocol)}' instead of 'h2'.", new AuthenticationException("Upstream did not negotiate HTTP/2."));
            }

            return tlsStream;
        }
        catch (Exception exception)
        {
            await tlsStream.DisposeAsync().ConfigureAwait(false);
            if ((exception is AuthenticationException or IOException) && exception is not UpstreamTlsException)
                throw new UpstreamTlsException($"TLS authentication failed for upstream '{endpoint.Name}'.", exception);
            throw;
        }
    }

    private static async ValueTask<IPAddress[]> ResolveAddressesAsync(UpstreamTransportEndpoint endpoint, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(endpoint.Address, out var address))
        {
            return[address];
        }

        return await Dns.GetHostAddressesAsync(endpoint.Address, cancellationToken).ConfigureAwait(false);
    }

    private static List<SslApplicationProtocol>? BuildApplicationProtocols(UpstreamTransportEndpoint endpoint)
    {
        if (RuntimeUpstreamProtocol.IsHttp3(endpoint.Protocol))
        {
            throw new InvalidOperationException("HTTP/3 upstreams use the QUIC upstream transport, not the TCP upstream connection factory.");
        }

        return RuntimeUpstreamProtocol.IsHttp2(endpoint.Protocol) ? [SslApplicationProtocol.Http2] : null;
    }

    private static string FormatNegotiatedProtocol(SslApplicationProtocol protocol)
    {
        if (protocol == SslApplicationProtocol.Http2)
        {
            return "h2";
        }

        if (protocol == SslApplicationProtocol.Http11)
        {
            return "http/1.1";
        }

        return protocol.Protocol.Length == 0 ? "none" : Encoding.ASCII.GetString(protocol.Protocol.Span);
    }
}

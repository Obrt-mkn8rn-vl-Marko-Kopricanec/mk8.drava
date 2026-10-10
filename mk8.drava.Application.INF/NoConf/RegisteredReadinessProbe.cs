using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Application.BLL.ControlPlane.Upstreams;
using Mk8.Drava.Application.INF.NodeRelay;
using Mk8.Drava.Application.INF.Proxy.Connections;
using Mk8.Drava.Contracts.Relay.V1;

namespace Mk8.Drava.Application.INF.NoConf;

public sealed class RegisteredReadinessProbe : IRegisteredReadinessProbe
{
    private readonly RegisteredRelayConnector? _relay;
    private readonly TimeSpan _timeout;
    public RegisteredReadinessProbe() : this(null, new RegistrationRuntimePolicy()) { }
    public RegisteredReadinessProbe(RegisteredRelayConnector relay) : this(relay, new RegistrationRuntimePolicy()) { }
    public RegisteredReadinessProbe(RegisteredRelayConnector? relay, RegistrationRuntimePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy); policy.Validate();
        _relay = relay; _timeout = TimeSpan.FromMilliseconds(policy.ReadinessTimeoutMilliseconds);
    }
    public async ValueTask<bool> CheckAsync(InstanceIntent intent, RuntimeUpstream upstream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(upstream);
        if (upstream.Membership != intent.Identity || !string.Equals(upstream.Address, intent.Address, StringComparison.Ordinal) || upstream.Port != intent.Port)
            throw new InvalidDataException("Readiness target differs from the compiled membership.");
        var address = IPAddress.Parse(intent.Address);
        var host = string.Equals(intent.Scheme, "https", StringComparison.Ordinal) ? upstream.EffectiveSniHost : intent.Address;
        if (IPAddress.TryParse(host, out var literal) && literal.AddressFamily == AddressFamily.InterNetworkV6) host = "[" + host + "]";
        var endpoint = UpstreamTransportEndpointMapper.FromUpstream(upstream);
        using var certificateTrust = UpstreamCertificateTrust.Load(endpoint);
        // A separate handler bounds each check and avoids retaining pools for expired boot identities.
        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false, UseProxy = false, UseCookies = false, MaxConnectionsPerServer = 1, MaxResponseHeadersLength = 16,
            ConnectCallback = async (_, token) =>
            {
                if (intent.Relay is not null)
                {
                    if (_relay is null) throw new IOException("Registered relay readiness requires its authority adapter.");
                    return await _relay.TryConnectAsync(UpstreamTransportEndpointMapper.FromUpstream(upstream), RelayPurpose.Readiness, token).ConfigureAwait(false)
                        ?? throw new IOException("Readiness relay did not return a transport.");
                }
                return await ConnectAsync(new IPEndPoint(address, intent.Port), token).ConfigureAwait(false);
            },
            SslOptions = certificateTrust.CreateOptions(endpoint, applicationProtocols: null),
        };
        using var client = new HttpClient(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"{intent.Scheme}://{host}:{intent.Port}{intent.ReadinessPath}"));
        request.Version = string.Equals(intent.Protocol, "http2", StringComparison.Ordinal) ? HttpVersion.Version20 : HttpVersion.Version11;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (HttpRequestException) { return false; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
    }

    private static async ValueTask<Stream> ConnectAsync(IPEndPoint endpoint, CancellationToken cancellationToken)
    {
        Socket? socket = new(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
            var stream = new NetworkStream(socket, ownsSocket: true);
            socket = null; // The returned stream owns the connected socket.
            return stream;
        }
        finally { socket?.Dispose(); }
    }
}

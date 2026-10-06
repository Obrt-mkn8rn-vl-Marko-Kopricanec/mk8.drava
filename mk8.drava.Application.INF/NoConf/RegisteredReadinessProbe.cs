using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.Registry;

namespace Mk8.Drava.Application.INF.NoConf;

public sealed class RegisteredReadinessProbe : IRegisteredReadinessProbe
{
    public async ValueTask<bool> CheckAsync(InstanceIntent intent, RuntimeUpstream upstream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(upstream);
        if (upstream.Membership != intent.Identity || !string.Equals(upstream.Address, intent.Address, StringComparison.Ordinal) || upstream.Port != intent.Port)
            throw new InvalidDataException("Readiness target differs from the compiled membership.");
        var address = IPAddress.Parse(intent.Address);
        var host = string.Equals(intent.Scheme, "https", StringComparison.Ordinal) ? upstream.EffectiveSniHost : intent.Address;
        if (IPAddress.TryParse(host, out var literal) && literal.AddressFamily == AddressFamily.InterNetworkV6) host = "[" + host + "]";
        // A separate handler bounds each check and avoids retaining pools for expired boot identities.
        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false, UseProxy = false, UseCookies = false, MaxConnectionsPerServer = 1, MaxResponseHeadersLength = 16,
            ConnectCallback = (_, token) => ConnectAsync(new IPEndPoint(address, intent.Port), token),
            SslOptions = new SslClientAuthenticationOptions
            {
                // This is the explicit, validated per-service setting also used by native forwarding. Secure by default.
                RemoteCertificateValidationCallback = (_, _, _, errors) => errors == SslPolicyErrors.None || !upstream.Tls.ValidateCertificate,
            },
        };
        using var client = new HttpClient(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"{intent.Scheme}://{host}:{intent.Port}{intent.ReadinessPath}"));
        request.Version = string.Equals(intent.Protocol, "http2", StringComparison.Ordinal) ? HttpVersion.Version20 : HttpVersion.Version11;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
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

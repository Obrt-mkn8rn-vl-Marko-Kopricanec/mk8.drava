using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using Mk8.Drava.Application.BLL.ControlPlane.Upstreams;

namespace Mk8.Drava.Application.INF.Proxy.Connections;
internal sealed class PooledUpstreamConnection : IDisposable
{
    public PooledUpstreamConnection(string key, UpstreamTransportEndpoint endpoint, Socket? socket, Stream stream, DateTimeOffset lastUsedUtc)
    {
        Key = key;
        Endpoint = endpoint;
        Socket = socket;
        Stream = stream;
        LastUsedUtc = lastUsedUtc;
    }

    public string Key { get; }
    public UpstreamTransportEndpoint Endpoint { get; }
    public Socket? Socket { get; }
    public Stream Stream { get; }
    public DateTimeOffset LastUsedUtc { get; private set; }
    public bool CanReturnToPool { get; private set; }
    public int MaxIdleConnections { get; private set; }

    public void MarkBorrowed(int maxIdleConnections)
    {
        CanReturnToPool = false;
        MaxIdleConnections = maxIdleConnections;
    }

    public bool IsIdleAndUsable
    {
        get
        {
            if (Socket is not { } socket) return false; // Relay capabilities authorize one exchange.
            try
            {
                return !socket.Poll(0, SelectMode.SelectRead) && !socket.Poll(0, SelectMode.SelectError);
            }
            catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
            {
                return false;
            }
        }
    }

    public async ValueTask<bool> PrepareForReuseAsync(CancellationToken cancellationToken)
    {
        if (!IsIdleAndUsable) return false;
        if (Stream is not SslStream tls) return true;
        using var probeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            var pending = tls.ReadAsync(new byte[1], probeCancellation.Token);
            if (!pending.IsCompleted) await probeCancellation.CancelAsync().ConfigureAwait(false);
            await pending.ConfigureAwait(false);
            return false; // Both unsolicited application data and TLS EOF forbid reuse.
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && probeCancellation.IsCancellationRequested)
        {
            return IsIdleAndUsable;
        }
        catch (Exception exception) when (exception is IOException or SocketException or AuthenticationException or ObjectDisposedException)
        {
            return false;
        }
    }

    public void MarkReusable()
    {
        CanReturnToPool = IsIdleAndUsable;
    }

    public void MarkUnusable()
    {
        CanReturnToPool = false;
    }

    public void MarkReturnedIdle(DateTimeOffset returnedAtUtc)
    {
        LastUsedUtc = returnedAtUtc;
        CanReturnToPool = false;
    }

    public void Dispose()
    {
        Stream.Dispose();
        Socket?.Dispose();
    }
}

using System.Net.Sockets;
using Mk8.Drava.Application.BLL.ControlPlane.Upstreams;

namespace Mk8.Drava.Application.INF.Proxy.Connections;
internal sealed class PooledUpstreamConnection : IDisposable
{
    public PooledUpstreamConnection(string key, UpstreamTransportEndpoint endpoint, Socket socket, Stream stream, DateTimeOffset lastUsedUtc)
    {
        Key = key;
        Endpoint = endpoint;
        Socket = socket;
        Stream = stream;
        LastUsedUtc = lastUsedUtc;
    }

    public string Key { get; }
    public UpstreamTransportEndpoint Endpoint { get; }
    public Socket Socket { get; }
    public Stream Stream { get; }
    public DateTimeOffset LastUsedUtc { get; private set; }
    public bool CanReturnToPool { get; private set; }
    public int MaxIdleConnections { get; private set; }

    public void MarkBorrowed(int maxIdleConnections)
    {
        CanReturnToPool = false;
        MaxIdleConnections = maxIdleConnections;
    }

    public void MarkReusable()
    {
        CanReturnToPool = true;
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
        Socket.Dispose();
    }
}

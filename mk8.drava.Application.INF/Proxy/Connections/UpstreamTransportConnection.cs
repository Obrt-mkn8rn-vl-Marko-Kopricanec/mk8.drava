using System.Net.Sockets;
using Mk8.Drava.Application.BLL.ControlPlane.Upstreams;

namespace Mk8.Drava.Application.INF.Proxy.Connections;
public sealed class UpstreamTransportConnection : IDisposable
{
    public UpstreamTransportConnection(UpstreamTransportEndpoint endpoint, Socket? socket, Stream stream)
    {
        Endpoint = endpoint;
        Socket = socket;
        Stream = stream;
    }

    public UpstreamTransportEndpoint Endpoint { get; }
    public Socket? Socket { get; }
    public Stream Stream { get; }

    public void Dispose()
    {
        try { Stream.Dispose(); }
        finally { Socket?.Dispose(); }
    }
}

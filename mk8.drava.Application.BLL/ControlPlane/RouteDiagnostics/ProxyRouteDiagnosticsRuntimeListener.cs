using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
public sealed class ProxyRouteDiagnosticsRuntimeListener : IProxyRouteDiagnosticsListener
{
    public ProxyRouteDiagnosticsRuntimeListener(RuntimeListener runtimeListener)
    {
        ArgumentNullException.ThrowIfNull(runtimeListener);
        Name = runtimeListener.Name;
        Transport = RuntimeListenerTransportScheme.FromTransport(runtimeListener.Transport);
        Address = runtimeListener.Address;
        Port = runtimeListener.Port;
        Enabled = runtimeListener.Enabled;
        SupportsHttp1 = runtimeListener.Protocols.HasFlag(RuntimeListenerProtocols.Http1);
        SupportsHttp2 = runtimeListener.Protocols.HasFlag(RuntimeListenerProtocols.Http2);
        SupportsHttp3 = runtimeListener.Protocols.HasFlag(RuntimeListenerProtocols.Http3);
        Http3EnabledForTraffic = runtimeListener.Http3.EnabledForTraffic;
    }

    public string Name { get; }
    public string Transport { get; }
    public string Address { get; }
    public int Port { get; }
    public bool Enabled { get; }
    public bool SupportsHttp1 { get; }
    public bool SupportsHttp2 { get; }
    public bool SupportsHttp3 { get; }
    public bool Http3EnabledForTraffic { get; }
}

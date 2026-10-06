using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
public sealed class ProxyRouteDiagnosticsRuntimeUpstream : IProxyRouteDiagnosticsUpstream
{
    public ProxyRouteDiagnosticsRuntimeUpstream(RuntimeUpstream runtimeUpstream)
    {
        ArgumentNullException.ThrowIfNull(runtimeUpstream);
        Name = runtimeUpstream.Name;
        Scheme = runtimeUpstream.Scheme;
        Protocol = runtimeUpstream.Protocol;
        Endpoint = runtimeUpstream.Endpoint;
        Weight = runtimeUpstream.Weight;
        CircuitBreakerEnabled = runtimeUpstream.CircuitBreaker.Enabled;
    }

    public string Name { get; }
    public string Scheme { get; }
    public string Protocol { get; }
    public string Endpoint { get; }
    public int Weight { get; }
    public bool CircuitBreakerEnabled { get; }
}

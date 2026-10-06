using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.UpstreamSelection;

namespace Mk8.Drava.Application.BLL.NoConf;

public sealed record ResolvedNoConfInputs
{
    public ResolvedNoConfInputs(UpstreamBalancingPolicy balancing, ProxyRouteOptions route, UpstreamTlsOptions tls,
        ProxyCircuitBreakerOptions circuit, IReadOnlyDictionary<string, string> provenance)
    {
        ArgumentNullException.ThrowIfNull(balancing);
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(tls);
        ArgumentNullException.ThrowIfNull(circuit);
        ArgumentNullException.ThrowIfNull(provenance);
        Balancing = balancing;
        Route = route;
        Tls = tls;
        Circuit = circuit;
        Provenance = RuntimeList.CopyDictionary(provenance, StringComparer.Ordinal);
    }
    public UpstreamBalancingPolicy Balancing { get; }
    public ProxyRouteOptions Route { get; }
    public UpstreamTlsOptions Tls { get; }
    public ProxyCircuitBreakerOptions Circuit { get; }
    public IReadOnlyDictionary<string, string> Provenance { get; }
}

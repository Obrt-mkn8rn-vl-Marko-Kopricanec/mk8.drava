using Mk8.Drava.Application.BLL.ControlPlane.Resilience;
using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.UpstreamSelection;
public sealed record UpstreamSelectionRoute
{
    public UpstreamSelectionRoute(string Name, bool HealthCheckEnabled, IReadOnlyList<RuntimeUpstream> Upstreams)
    {
        ArgumentNullException.ThrowIfNull(Name);
        this.Name = Name;
        this.HealthCheckEnabled = HealthCheckEnabled;
        this.Upstreams = RuntimeList.Copy(Upstreams);
    }

    public string Name { get; }
    public bool HealthCheckEnabled { get; }
    public IReadOnlyList<RuntimeUpstream> Upstreams { get; }
    public UpstreamBalancingPolicy Policy { get; init; } = new(BalancingAlgorithm.WeightedRoundRobin);
}

using Mk8.Drava.Application.BLL.ControlPlane.Resilience;
using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
using Mk8.Drava.Application.BLL.ControlPlane.Upstreams;
using System.Collections.Concurrent;
using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.UpstreamSelection;
public sealed class RoundRobinUpstreamSelector : IUpstreamSelector
{
    private readonly UpstreamHealthStore _healthStore;
    private readonly CircuitBreakerStore _circuitBreakerStore;
    private readonly IProxyUpstreamSelectionMetricsSink _metrics;
    private readonly ConcurrentDictionary<string, int> _nextIndexes = new(StringComparer.OrdinalIgnoreCase);
    public RoundRobinUpstreamSelector(UpstreamHealthStore healthStore, CircuitBreakerStore circuitBreakerStore, IProxyUpstreamSelectionMetricsSink metrics)
    {
        _healthStore = healthStore;
        _circuitBreakerStore = circuitBreakerStore;
        _metrics = metrics;
    }

    public SelectedUpstream? Select(UpstreamSelectionRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (route.Upstreams.Count == 0)
        {
            _metrics.NoHealthyUpstream();
            _metrics.NoAvailableUpstream();
            return null;
        }

        List<RuntimeUpstream> candidates = [];
        foreach (var upstream in route.Upstreams)
        {
            var circuitSource = CircuitBreakerStatusSourceMapper.FromUpstream(upstream);
            var healthSource = UpstreamHealthStateSourceMapper.FromUpstream(upstream);
            if (route.HealthCheckEnabled && !_healthStore.IsUsable(healthSource))
            {
                continue;
            }

            if (_circuitBreakerStore.IsAvailable(circuitSource))
            {
                candidates.Add(upstream);
                continue;
            }

            _circuitBreakerStore.RecordRejectedIfUnavailable(circuitSource);
        }

        if (candidates.Count == 0)
        {
            _metrics.NoHealthyUpstream();
            _metrics.NoAvailableUpstream();
            return null;
        }

        while (candidates.Count > 0)
        {
            var selected = SelectWeighted(route, candidates);
            var acquisition = _circuitBreakerStore.Acquire(CircuitBreakerStatusSourceMapper.FromUpstream(selected));
            if (acquisition is CircuitBreakerAcquisitionResult.AcceptedResult acceptedAcquisition)
            {
                _metrics.UpstreamSelected(new ProxyUpstreamSelectionMetric(selected.RouteName, selected.Name, selected.Scheme, selected.Protocol));
                _healthStore.RecordSelection(UpstreamHealthStateSourceMapper.FromUpstream(selected));
                return new SelectedUpstream(selected, acceptedAcquisition.Lease);
            }

            candidates.Remove(selected);
        }

        _metrics.NoAvailableUpstream();
        return null;
    }

    private RuntimeUpstream SelectWeighted(UpstreamSelectionRoute route, List<RuntimeUpstream> candidates)
    {
        var totalWeight = candidates.Sum(static upstream => upstream.Weight);
        if (totalWeight <= 0)
        {
            return candidates[0];
        }

        var index = _nextIndexes.AddOrUpdate(route.Name, 1, (_, current) => current == int.MaxValue ? 0 : current + 1);
        var position = Math.Abs(index - 1) % totalWeight;
        var cumulative = 0;
        foreach (ref var upstream in System.Runtime.InteropServices.CollectionsMarshal.AsSpan(candidates))
        {
            cumulative += upstream.Weight;
            if (position < cumulative)
            {
                return upstream;
            }
        }

        return candidates[^1];
    }
}

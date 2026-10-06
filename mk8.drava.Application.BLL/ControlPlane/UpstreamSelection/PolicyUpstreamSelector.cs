using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
using Mk8.Drava.Application.BLL.ControlPlane.Resilience;
using Mk8.Drava.Application.BLL.ControlPlane.Upstreams;
using Mk8.Drava.Application.BLL.Registry;

namespace Mk8.Drava.Application.BLL.ControlPlane.UpstreamSelection;

public sealed class PolicyUpstreamSelector : IUpstreamReservationSelector
{
    private readonly DestinationAvailabilityStore _availability;
    private readonly UpstreamHealthStore _health;
    private readonly CircuitBreakerStore _circuits;
    private readonly IProxyUpstreamSelectionMetricsSink _metrics;
    private readonly ConditionalWeakTable<UpstreamSelectionRoute, SelectionSequence> _sequences = new();

    public PolicyUpstreamSelector(DestinationAvailabilityStore availability, UpstreamHealthStore health, CircuitBreakerStore circuits, IProxyUpstreamSelectionMetricsSink metrics)
    {
        ArgumentNullException.ThrowIfNull(availability);
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(circuits);
        ArgumentNullException.ThrowIfNull(metrics);
        _availability = availability;
        _health = health;
        _circuits = circuits;
        _metrics = metrics;
    }

    public UpstreamReservation? Reserve(UpstreamSelectionRoute route, string? affinityKey)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (affinityKey?.Length > 512) throw new InvalidDataException("Affinity key exceeds its limit.");
        var policy = route.Policy;
        if (policy.Algorithm == BalancingAlgorithm.StableHash && string.IsNullOrEmpty(affinityKey))
            throw new InvalidDataException("The configured affinity key is missing.");
        lock (_availability.Gate)
        {
            var candidates = FindCandidates(route);
            ApplyLocality(candidates, policy);
            while (candidates.Count > 0)
            {
                var index = Choose(candidates, policy.Algorithm, route, affinityKey);
                var candidate = candidates[index];
                var acquisition = _circuits.Acquire(CircuitBreakerStatusSourceMapper.FromUpstream(candidate.Upstream));
                if (acquisition is not CircuitBreakerAcquisitionResult.AcceptedResult accepted)
                {
                    candidates.RemoveAt(index);
                    continue;
                }
                if (_availability.FindEligible(candidate.Upstream) is null)
                {
                    accepted.Lease.Dispose();
                    candidates.RemoveAt(index);
                    continue;
                }
                try
                {
                    _metrics.UpstreamSelected(new ProxyUpstreamSelectionMetric(candidate.Upstream.RouteName, candidate.Upstream.Name, candidate.Upstream.Scheme, candidate.Upstream.Protocol));
                    _health.RecordSelection(UpstreamHealthStateSourceMapper.FromUpstream(candidate.Upstream));
                    var reservation = new UpstreamReservation(new SelectedUpstream(candidate.Upstream, accepted.Lease), _availability, candidate.Destination);
                    candidate.Destination.Active = checked(candidate.Destination.Active + 1);
                    return reservation;
                }
                catch { accepted.Lease.Dispose(); throw; }
            }
            _metrics.NoHealthyUpstream();
            _metrics.NoAvailableUpstream();
            return null;
        }
    }

    private List<Candidate> FindCandidates(UpstreamSelectionRoute route)
    {
        var candidates = new List<Candidate>(route.Upstreams.Count);
        foreach (var upstream in route.Upstreams)
        {
            var destination = _availability.FindEligible(upstream);
            if (destination is null || (route.HealthCheckEnabled && !_health.IsUsable(UpstreamHealthStateSourceMapper.FromUpstream(upstream)))) continue;
            var circuit = CircuitBreakerStatusSourceMapper.FromUpstream(upstream);
            if (_circuits.IsAvailable(circuit)) candidates.Add(new Candidate(upstream, destination));
            else _circuits.RecordRejectedIfUnavailable(circuit);
        }
        return candidates;
    }

    private static void ApplyLocality(List<Candidate> candidates, UpstreamBalancingPolicy policy)
    {
        if (policy.PreferredZone is null) return;
        var hasLocal = false;
        for (var index = 0; index < candidates.Count; index++)
            if (string.Equals(candidates[index].Destination.Intent?.Zone, policy.PreferredZone, StringComparison.Ordinal)) { hasLocal = true; break; }
        if (hasLocal || policy.RequireLocalZone)
            candidates.RemoveAll(candidate => !string.Equals(candidate.Destination.Intent?.Zone, policy.PreferredZone, StringComparison.Ordinal));
    }

    private int Choose(List<Candidate> candidates, BalancingAlgorithm algorithm, UpstreamSelectionRoute route, string? key)
    {
        if (candidates.Count == 1) return 0;
        switch (algorithm)
        {
            case BalancingAlgorithm.PowerOfTwoChoices:
                var first = PickWeighted(candidates, NextPosition(TotalWeight(candidates)), excluded: -1);
                var second = PickWeighted(candidates, NextPosition(TotalWeight(candidates) - candidates[first].Upstream.Weight), excluded: first);
                return LessActive(candidates[second], candidates[first]) ? second : first;
            case BalancingAlgorithm.WeightedRoundRobin:
                var sequence = _sequences.GetValue(route, static _ => new SelectionSequence());
                var position = sequence.Next++ % (ulong)TotalWeight(candidates);
                return PickWeighted(candidates, (long)position, excluded: -1);
            case BalancingAlgorithm.LeastActive:
                var least = PickWeighted(candidates, NextPosition(TotalWeight(candidates)), excluded: -1);
                for (var index = 0; index < candidates.Count; index++)
                    if (LessActive(candidates[index], candidates[least])) least = index;
                return least;
            case BalancingAlgorithm.StableHash:
                return Rendezvous(candidates, key!);
            default: throw new InvalidDataException("Unsupported balancing algorithm.");
        }
    }

    private static bool LessActive(Candidate left, Candidate right) => (long)left.Destination.Active * right.Upstream.Weight < (long)right.Destination.Active * left.Upstream.Weight;

    private static long TotalWeight(List<Candidate> candidates)
    {
        long total = 0;
        for (var index = 0; index < candidates.Count; index++) total += candidates[index].Upstream.Weight;
        return total;
    }

    private static long NextPosition(long maximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximum);
        var modulus = (ulong)maximum;
        var limit = ulong.MaxValue - ulong.MaxValue % modulus;
        Span<byte> bytes = stackalloc byte[8];
        ulong value;
        do
        {
            RandomNumberGenerator.Fill(bytes);
            value = System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(bytes);
        } while (value >= limit);
        return (long)(value % modulus);
    }

    private static int PickWeighted(List<Candidate> candidates, long position, int excluded)
    {
        for (var index = 0; index < candidates.Count; index++)
        {
            if (index == excluded) continue;
            position -= candidates[index].Upstream.Weight;
            if (position < 0) return index;
        }
        throw new InvalidOperationException("Invalid weighted candidate position.");
    }

    private static int Rendezvous(List<Candidate> candidates, string key)
    {
        var winner = 0;
        var best = double.PositiveInfinity;
        for (var index = 0; index < candidates.Count; index++)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key + "\0" + candidates[index].Upstream.Identity));
            var value = System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(hash) >> 11;
            var unit = (value + 1.0) / (9007199254740992.0 + 2.0);
            var score = -Math.Log(unit) / candidates[index].Upstream.Weight;
            if (score < best) { best = score; winner = index; }
        }
        return winner;
    }

    private sealed record Candidate(RuntimeUpstream Upstream, DestinationAvailability Destination);
    private sealed class SelectionSequence { public ulong Next { get; set; } }
}

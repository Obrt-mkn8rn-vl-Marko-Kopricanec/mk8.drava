using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Contracts.Registration.V1;
using Mk8.Drava.Contracts.Relay.V1;

namespace Mk8.Drava.Application.BLL.NodeRelay;

// Only the private, authenticated local SDK adapter may write mappings. Remote capabilities only select an existing mapping.
public sealed class NodeRelayMappings
{
    private const int MaximumMappings = 10_000;
    private const int MaximumReplays = 65_536;
    private const int MaximumRetiredBoots = 100_000;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Mapping> _mappings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _consumed = new(StringComparer.Ordinal);
    private readonly HashSet<string> _retiredBoots = new(StringComparer.Ordinal);
    private readonly HashSet<string> _localAddresses;
    private readonly string _siteId;
    private readonly string _agentBootId;
    private readonly NodeGrant _grant;
    private readonly TimeProvider _clock;
    private readonly long _startedAt;
    private readonly DateTimeOffset _startedUtc;
    private DateTimeOffset _utcFloor;
    private long _lastPrunedAt;
    private bool _revoked;

    public NodeRelayMappings(string siteId, string agentBootId, NodeGrant grant, IEnumerable<string> localAddresses, TimeProvider clock)
    {
        RegistryNames.RequireLabel(siteId);
        RegistryNames.RequireEpoch(agentBootId);
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentNullException.ThrowIfNull(localAddresses);
        ArgumentNullException.ThrowIfNull(clock);
        _siteId = siteId;
        _agentBootId = agentBootId;
        _grant = grant;
        _clock = clock;
        _localAddresses = new HashSet<string>(localAddresses, StringComparer.Ordinal);
        _startedAt = clock.GetTimestamp();
        _lastPrunedAt = _startedAt;
        _startedUtc = clock.GetUtcNow();
        _utcFloor = _startedUtc;
    }

    public void Renew(RegistrationIdentity identity, ServiceAdvertisement advertisement)
    {
        var intent = ToIntent(identity, advertisement);
        lock (_gate)
        {
            RequireScope(identity, intent);
            Prune();
            var boot = intent.Identity.Partition;
            if (_retiredBoots.Contains(boot)) throw new UnauthorizedAccessException("Retired local boot cannot return.");
            if (_mappings.TryGetValue(identity.InstanceId, out var previous))
            {
                if (!SameOwner(previous.Intent.Identity, intent.Identity)) throw new UnauthorizedAccessException("Local instance ownership cannot change.");
                if (string.Equals(previous.Identity.BootId, identity.BootId, StringComparison.Ordinal))
                {
                    if (previous.Advertisement != advertisement) throw new UnauthorizedAccessException("Local endpoint cannot change within a boot.");
                }
                else Retire(previous.Intent.Identity.Partition);
            }
            else if (_mappings.Count >= MaximumMappings) throw new InvalidOperationException("Local relay mapping capacity is exhausted.");
            _mappings[identity.InstanceId] = new Mapping(identity, advertisement, intent, _clock.GetTimestamp());
        }
    }

    public void Drain(RegistrationIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        lock (_gate)
        {
            if (!_mappings.TryGetValue(identity.InstanceId, out var mapping) || mapping.Identity != identity) return;
            Retire(mapping.Intent.Identity.Partition);
            _mappings.Remove(identity.InstanceId);
        }
    }

    public InstanceIntent AuthorizeAndConsume(RelayCapability capability)
    {
        ArgumentNullException.ThrowIfNull(capability);
        lock (_gate)
        {
            Prune();
            var now = EffectiveUtc().ToUnixTimeMilliseconds();
            if (_revoked || _grant.Revoked || EffectiveUtc() >= _grant.NotAfterUtc ||
                !string.Equals(capability.AgentBootId, _agentBootId, StringComparison.Ordinal) ||
                capability.IssuedAtUnixMilliseconds > now + 1000 || capability.ExpiresAtUnixMilliseconds <= now ||
                capability.ExpiresAtUnixMilliseconds - capability.IssuedAtUnixMilliseconds is <= 0 or > 15_000)
                throw new UnauthorizedAccessException("Relay capability or local authority is expired or mismatched.");
            if (!_mappings.TryGetValue(capability.Identity.InstanceId, out var mapping) || _clock.GetElapsedTime(mapping.RenewedAt) >= TimeSpan.FromSeconds(90) || mapping.Identity != capability.Identity ||
                mapping.Advertisement != capability.Advertisement) throw new UnauthorizedAccessException("Relay capability does not select a live local mapping.");
            RequireScope(mapping.Identity, mapping.Intent);
            var replayKey = capability.ControllerEpoch + "|" + capability.CapabilityId;
            if (_consumed.ContainsKey(replayKey)) throw new UnauthorizedAccessException("Relay capability has already been consumed.");
            if (_consumed.Count >= MaximumReplays) throw new InvalidOperationException("Relay replay capacity is exhausted.");
            _consumed.Add(replayKey, _clock.GetTimestamp());
            return mapping.Intent;
        }
    }

    public void Revoke()
    {
        lock (_gate) { _revoked = true; _mappings.Clear(); }
    }

    private void RequireScope(RegistrationIdentity identity, InstanceIntent intent)
    {
        if (_revoked || !string.Equals(identity.SiteId, _siteId, StringComparison.Ordinal) ||
            !_grant.Authorizes(intent, EffectiveUtc()) || !_localAddresses.Contains(intent.Address))
            throw new UnauthorizedAccessException("Local mapping is outside this node's enrollment and interface scope.");
    }

    private DateTimeOffset EffectiveUtc()
    {
        var elapsedUtc = _startedUtc + _clock.GetElapsedTime(_startedAt);
        var actualUtc = _clock.GetUtcNow();
        if (elapsedUtc > _utcFloor) _utcFloor = elapsedUtc;
        if (actualUtc > _utcFloor) _utcFloor = actualUtc;
        return _utcFloor;
    }

    private void Prune()
    {
        if (_clock.GetElapsedTime(_lastPrunedAt) < TimeSpan.FromSeconds(1)) return;
        _lastPrunedAt = _clock.GetTimestamp();
        foreach (var key in _mappings.Where(pair => _clock.GetElapsedTime(pair.Value.RenewedAt) >= TimeSpan.FromSeconds(90)).Select(static pair => pair.Key).ToArray())
        {
            _mappings.Remove(key);
        }
        foreach (var key in _consumed.Where(pair => _clock.GetElapsedTime(pair.Value) >= TimeSpan.FromSeconds(16)).Select(static pair => pair.Key).ToArray()) _consumed.Remove(key);
    }

    private void Retire(string boot)
    {
        if (_retiredBoots.Contains(boot)) return;
        if (_retiredBoots.Count >= MaximumRetiredBoots) { _revoked = true; _mappings.Clear(); throw new InvalidOperationException("Local boot retirement capacity is exhausted."); }
        _retiredBoots.Add(boot);
    }

    private static bool SameOwner(RegisteredUpstreamIdentity left, RegisteredUpstreamIdentity right) =>
        string.Equals(left.NodeId, right.NodeId, StringComparison.Ordinal) && string.Equals(left.OwnerId, right.OwnerId, StringComparison.Ordinal) &&
        string.Equals(left.ServiceId, right.ServiceId, StringComparison.Ordinal) && string.Equals(left.ContractId, right.ContractId, StringComparison.Ordinal) &&
        string.Equals(left.InstanceId, right.InstanceId, StringComparison.Ordinal);

    private static InstanceIntent ToIntent(RegistrationIdentity identity, ServiceAdvertisement advertisement)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(advertisement);
        return new InstanceIntent(new RegisteredUpstreamIdentity(identity.NodeId, identity.OwnerId, identity.ServiceId, identity.ContractId, identity.InstanceId, identity.BootId),
            advertisement.DeploymentId, advertisement.Address, advertisement.Port, advertisement.Protocol, advertisement.Scheme, advertisement.ReadinessPath,
            advertisement.Zone, advertisement.Weight, draining: false, advertisement.Relay);
    }

    private sealed record Mapping(RegistrationIdentity Identity, ServiceAdvertisement Advertisement, InstanceIntent Intent, long RenewedAt);
}

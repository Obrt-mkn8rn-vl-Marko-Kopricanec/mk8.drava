using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.Registry;

// The selection gate is shared with lease/readiness/revocation updates. Counts belong to HTTP exchanges, not connections.
public sealed class DestinationAvailabilityStore(TimeProvider clock)
{
    private readonly Dictionary<string, DestinationAvailability> _destinations = new(StringComparer.Ordinal);
    private bool _authorityValid = true;
    internal Lock Gate { get; } = new();

    public static void ValidateLease(TimeSpan lease)
    {
        if (lease < TimeSpan.FromSeconds(15) || lease > TimeSpan.FromMinutes(5)) throw new InvalidDataException("Lease is outside site bounds.");
    }

    public void Renew(InstanceIntent intent, DateTimeOffset credentialNotAfterUtc, TimeSpan lease)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ValidateLease(lease);
        lock (Gate)
        {
            if (!_authorityValid) throw new InvalidOperationException("Registered authority requires a restart after storage failure.");
            var destination = GetOrCreate("registered|" + intent.Identity.Partition);
            // Expired membership requires a new readiness proof; an old successful probe cannot resurrect it.
            if (!destination.LeaseValid(clock) || destination.Intent != intent)
            {
                destination.Ready = false;
                destination.Publication = null;
                destination.ReadinessGeneration = checked(destination.ReadinessGeneration + 1);
            }
            destination.Intent = intent;
            destination.RenewedAt = clock.GetTimestamp();
            destination.Lease = lease;
            destination.CredentialNotAfterUtc = credentialNotAfterUtc;
            destination.Revoked = intent.Draining;
        }
    }

    public bool SetReadiness(RegisteredUpstreamIdentity identity, bool ready, TimeSpan validity, long? expectedGeneration = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (validity < TimeSpan.FromSeconds(1) || validity > TimeSpan.FromMinutes(5)) throw new InvalidDataException("Invalid readiness proof lifetime.");
        lock (Gate)
        {
            if (!_destinations.TryGetValue("registered|" + identity.Partition, out var destination) || !destination.LeaseValid(clock) || (expectedGeneration is { } generation && generation != destination.ReadinessGeneration)) return false;
            destination.Ready = ready;
            destination.CheckedAt = clock.GetTimestamp();
            destination.ProofValidity = validity;
            return true;
        }
    }

    public bool SetPublication(RegisteredUpstreamIdentity identity, DestinationPublication publication, long? expectedGeneration = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(publication);
        lock (Gate)
        {
            if (!_destinations.TryGetValue("registered|" + identity.Partition, out var destination) || !destination.LeaseValid(clock) || (expectedGeneration is { } generation && generation != destination.ReadinessGeneration)) return false;
            destination.Publication = publication;
            destination.PublicationAt = clock.GetTimestamp();
            var validity = publication.ValidUntilUtc - clock.GetUtcNow();
            destination.PublicationValidity = validity > TimeSpan.FromMinutes(5) ? TimeSpan.FromMinutes(5) : validity;
            return true;
        }
    }

    public void InvalidateReadiness(RegisteredUpstreamIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        lock (Gate)
            if (_destinations.TryGetValue("registered|" + identity.Partition, out var destination))
            {
                destination.Ready = false;
                destination.Publication = null;
                destination.ReadinessGeneration = checked(destination.ReadinessGeneration + 1);
            }
    }

    public void ClearPublication(RegisteredUpstreamIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        lock (Gate)
            if (_destinations.TryGetValue("registered|" + identity.Partition, out var destination)) destination.Publication = null;
    }

    public void Revoke(RegisteredUpstreamIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        lock (Gate)
            if (_destinations.TryGetValue("registered|" + identity.Partition, out var destination)) destination.Revoked = true;
    }

    public void RevokeNode(string nodeId)
    {
        RegistryNames.RequireLabel(nodeId);
        lock (Gate)
            foreach (var destination in _destinations.Values)
                if (string.Equals(destination.Intent?.Identity.NodeId, nodeId, StringComparison.Ordinal)) destination.Revoked = true;
    }

    public void FailClosed()
    {
        lock (Gate)
        {
            _authorityValid = false;
            foreach (var destination in _destinations.Values)
                if (destination.Intent is not null) destination.Revoked = true;
        }
    }

    public bool IsEligible(RegisteredUpstreamIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        lock (Gate)
            return _destinations.TryGetValue("registered|" + identity.Partition, out var destination) && destination.IsEligible(clock);
    }

    public bool HasEligibleRegisteredUpstream(IReadOnlyList<RuntimeUpstream> upstreams)
    {
        ArgumentNullException.ThrowIfNull(upstreams);
        lock (Gate)
            foreach (var upstream in upstreams)
                if (upstream.Membership is not null && FindEligible(upstream) is not null) return true;
        return false;
    }

    public int ActiveRequests(RuntimeUpstream upstream)
    {
        ArgumentNullException.ThrowIfNull(upstream);
        lock (Gate)
            return _destinations.TryGetValue(Key(upstream), out var destination) ? destination.Active : 0;
    }

    public DestinationStatus Status(RegisteredUpstreamIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        lock (Gate)
        {
            if (!_destinations.TryGetValue("registered|" + identity.Partition, out var destination)) return new(false, false, false, false);
            var age = clock.GetElapsedTime(destination.CheckedAt);
            return new(destination.LeaseValid(clock), destination.Ready && age >= TimeSpan.Zero && age < destination.ProofValidity,
                destination.Publication?.IsValid(clock.GetUtcNow()) == true && clock.GetElapsedTime(destination.PublicationAt) >= TimeSpan.Zero && clock.GetElapsedTime(destination.PublicationAt) < destination.PublicationValidity, destination.Revoked, destination.Publication, destination.ReadinessGeneration);
        }
    }

    internal DestinationAvailability? FindEligible(RuntimeUpstream upstream)
    {
        // Caller holds Gate through candidate comparison, circuit acquisition and active reservation.
        var key = Key(upstream);
        if (upstream.Membership is null) return GetOrCreate(key);
        return _destinations.TryGetValue(key, out var destination) && destination.IsEligible(clock) && destination.Intent is { } intent &&
            string.Equals(intent.Address, upstream.Address, StringComparison.Ordinal) && intent.Port == upstream.Port &&
            string.Equals(intent.Protocol, upstream.Protocol, StringComparison.Ordinal) && string.Equals(intent.Scheme, upstream.Scheme, StringComparison.Ordinal) ? destination : null;
    }

    internal void Release(DestinationAvailability destination)
    {
        lock (Gate)
        {
            if (destination.Active <= 0) throw new InvalidOperationException("Active request reservation underflow.");
            destination.Active--;
        }
    }

    private static string Key(RuntimeUpstream upstream) => upstream.Membership is null ? "manual|" + upstream.Identity : "registered|" + upstream.Membership.Partition;

    private DestinationAvailability GetOrCreate(string key)
    {
        if (_destinations.TryGetValue(key, out var existing)) return existing;
        if (_destinations.Count >= 100_000) throw new InvalidOperationException("Destination capacity exceeded.");
        var destination = new DestinationAvailability();
        _destinations.Add(key, destination);
        return destination;
    }
}

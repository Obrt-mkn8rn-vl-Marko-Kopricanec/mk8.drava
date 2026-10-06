using Mk8.Drava.Application.BLL.Configuration;
using System.Collections.Frozen;

namespace Mk8.Drava.Application.BLL.Registry;

// Durable intent only. Leases, active requests and readiness proofs never enter a backup or this record.
public sealed record RegistryState
{
    private readonly FrozenSet<string> _revokedBoots;
    public RegistryState(long revision, IReadOnlyDictionary<string, NodeGrant> grants,
        IReadOnlyDictionary<string, InstanceIntent> instances, IReadOnlyList<string> tombstones)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(revision);
        ArgumentNullException.ThrowIfNull(grants);
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(tombstones);
        if (grants.Count > 1024 || instances.Count > 10_000 || tombstones.Count > 100_000)
            throw new InvalidDataException("Registry capacity exceeded.");
        Revision = revision;
        Grants = RuntimeList.CopyDictionary(grants, StringComparer.Ordinal);
        Instances = RuntimeList.CopyDictionary(instances, StringComparer.Ordinal);
        Tombstones = RuntimeList.Copy(tombstones);
        _revokedBoots = tombstones.ToFrozenSet(StringComparer.Ordinal);
    }

    public static RegistryState Empty { get; } = new(0, new Dictionary<string, NodeGrant>(StringComparer.Ordinal), new Dictionary<string, InstanceIntent>(StringComparer.Ordinal), []);
    public long Revision { get; }
    public IReadOnlyDictionary<string, NodeGrant> Grants { get; }
    public IReadOnlyDictionary<string, InstanceIntent> Instances { get; }
    public IReadOnlyList<string> Tombstones { get; }
    public bool IsTombstoned(RegisteredUpstreamIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return _revokedBoots.Contains(identity.Partition);
    }
}

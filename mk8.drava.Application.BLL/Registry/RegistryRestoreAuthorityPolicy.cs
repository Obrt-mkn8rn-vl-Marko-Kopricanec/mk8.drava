namespace Mk8.Drava.Application.BLL.Registry;

public static class RegistryRestoreAuthorityPolicy
{
    // An image cannot prove revocations that happened after it was captured. Keep ownership,
    // but require approved enrollment again and never admit a recorded boot after recovery.
    public static RegistryState CloseImportedAuthority(RegistryState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var grants = new Dictionary<string, NodeGrant>(StringComparer.Ordinal);
        foreach (var pair in state.Grants) grants.Add(pair.Key, pair.Value.Revoke());
        var instances = new Dictionary<string, InstanceIntent>(StringComparer.Ordinal);
        var tombstones = new List<string>(state.Tombstones);
        foreach (var pair in state.Instances)
        {
            instances.Add(pair.Key, pair.Value.Drain());
            if (!state.IsTombstoned(pair.Value.Identity)) tombstones.Add(pair.Value.Identity.Partition);
        }
        // Capacity and revision exhaustion refuse recovery; never discard revocation history.
        return new RegistryState(checked(state.Revision + 1), grants, instances, tombstones);
    }
}

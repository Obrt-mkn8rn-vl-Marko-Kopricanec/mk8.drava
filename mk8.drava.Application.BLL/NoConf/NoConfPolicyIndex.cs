using System.Collections.Frozen;
using Mk8.Drava.Application.BLL.Registry;

namespace Mk8.Drava.Application.BLL.NoConf;

internal sealed class NoConfPolicyIndex
{
    public NoConfPolicyIndex(NoConfPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (policy.Version != 1 || policy.Mode is not "auto" and not "hybrid" and not "manual" ||
            policy.Global is null || policy.Site is null || policy.Services is null || policy.Services.Count > 10_000)
            throw new InvalidDataException("Invalid noconf policy schema or mode.");
        Policy = policy;
        var services = new Dictionary<string, NoConfServicePolicy>(StringComparer.Ordinal);
        foreach (var candidate in policy.Services)
        {
            if (candidate is null || candidate.Profile is null || candidate.Route is null) throw new InvalidDataException("Service policy cannot contain null entries.");
            RegistryNames.RequireLabel(candidate.ServiceId);
            if (!services.TryAdd(candidate.ServiceId, candidate)) throw new InvalidDataException("Duplicate service policy.");
        }
        Services = services.ToFrozenDictionary(StringComparer.Ordinal);
    }

    public NoConfPolicy Policy { get; }
    public FrozenDictionary<string, NoConfServicePolicy> Services { get; }
}

namespace Mk8.Drava.Contracts.Administration.V1;

public sealed class PolicyStateResponse
{
    public long AcceptedRevision { get; init; }
    public long AppliedRevision { get; init; }
    public int RuntimeVersion { get; init; }
    public long RegistryRevision { get; init; }
    public string Digest { get; init; } = "";
    public string Source { get; init; } = "";
    public string CanonicalJson { get; init; } = "";
    public string Failure { get; init; } = "";
    public IReadOnlyList<PolicyRevisionResponse> History { get; init; } = [];
    public IReadOnlyList<ServicePolicyResponse> Services { get; init; } = [];
    public string NextServiceId { get; init; } = "";
}

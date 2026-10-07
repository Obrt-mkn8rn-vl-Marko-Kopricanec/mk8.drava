namespace Mk8.Drava.Contracts.Administration.V1;

public sealed class ServicePolicyResponse
{
    public string ServiceId { get; init; } = "";
    public string Host { get; init; } = "";
    public string PathPrefix { get; init; } = "";
    public string Action { get; init; } = "";
    public string Algorithm { get; init; } = "";
    public IReadOnlyDictionary<string, string> Provenance { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
}

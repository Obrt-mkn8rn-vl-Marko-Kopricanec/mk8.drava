namespace Mk8.Drava.Contracts.Administration.V1;

public sealed class NodeGrantResponse
{
    public string NodeId { get; init; } = "";
    public string OwnerId { get; init; } = "";
    public string CertificateFingerprint { get; init; } = "";
    public string ServicePrefix { get; init; } = "";
    public IReadOnlyList<string> EndpointAddresses { get; init; } = [];
    public int MinimumPort { get; init; }
    public int MaximumPort { get; init; }
    public DateTimeOffset NotAfterUtc { get; init; }
    public bool Revoked { get; init; }
}

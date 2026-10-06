namespace Mk8.Drava.Contracts.Registration.V1;

public sealed record NodeRelayEndpoint
{
    public string AgentBootId { get; init; } = "";
    public string Address { get; init; } = "";
    public int Port { get; init; }
    public string CertificateFingerprint { get; init; } = "";
}

namespace Mk8.Drava.Configuration;

public sealed record NodeAgentDescriptor
{
    public uint Version { get; init; } = 1;
    public string SiteId { get; init; } = "";
    public string NodeId { get; init; } = "";
    public string AgentBootId { get; init; } = "";
    public string RelayAddress { get; init; } = "";
    public int RelayPort { get; init; }
    public int MappingLeaseSeconds { get; init; } = 90;
    public string CertificateFingerprint { get; init; } = "";
    public IpcEndpoint LocalEndpoint { get; init; } = new();

    public void Validate()
    {
        RegistrationSiteTrust.RequireLabel(SiteId); RegistrationSiteTrust.RequireLabel(NodeId);
        LocalEndpoint.Validate(); NodeAgentBootstrap.RequireAddress(RelayAddress);
        if (Version != 1 || LocalEndpoint.HttpsAddress.Length != 0 || RelayPort is < 1 or > 65535 || MappingLeaseSeconds is < 15 or > 300 ||
            !Guid.TryParseExact(AgentBootId, "N", out var epoch) || epoch == Guid.Empty || !string.Equals(epoch.ToString("N"), AgentBootId, StringComparison.Ordinal) ||
            CertificateFingerprint.Length != 64 || CertificateFingerprint.Any(static value => value is not (>= '0' and <= '9') and not (>= 'A' and <= 'F')))
            throw new InvalidDataException("Invalid local node-agent descriptor.");
    }
}

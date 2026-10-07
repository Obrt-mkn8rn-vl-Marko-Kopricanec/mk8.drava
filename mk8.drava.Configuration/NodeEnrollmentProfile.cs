namespace Mk8.Drava.Configuration;

public sealed record NodeEnrollmentProfile
{
    public int SchemaVersion { get; init; } = 1;
    public RegistrationSiteTrust Site { get; init; } = new();
    public string NodeId { get; init; } = "";
    public string OwnerId { get; init; } = "";
    public string ServicePrefix { get; init; } = "";
    public IReadOnlyList<string> GatewayAddresses { get; init; } = [];
    public int RegistrationPort { get; init; } = 9443;
    public bool MulticastDiscovery { get; init; } = true;

    public void Validate()
    {
        if (SchemaVersion != 1 || GatewayAddresses.Count > 16 || RegistrationPort is < 1 or > 65535)
            throw new InvalidDataException("Invalid node enrollment profile.");
        Site.Validate();
        RegistrationSiteTrust.RequireLabel(NodeId);
        RegistrationSiteTrust.RequireLabel(OwnerId);
        RegistrationSiteTrust.RequireLabel(ServicePrefix);
        foreach (var address in GatewayAddresses) NodeAgentBootstrap.RequireAddress(address);
        if (!MulticastDiscovery && GatewayAddresses.Count == 0)
            throw new InvalidDataException("Enrollment requires discovery or an explicit Gateway address.");
    }
}

using Mk8.Drava.Configuration;

namespace Mk8.Drava.Application.Installation;

internal sealed record SiteInitializationOptions
{
    public int SchemaVersion { get; init; } = 1;
    public string DestinationDirectory { get; init; } = "";
    public string SiteId { get; init; } = "";
    public string Domain { get; init; } = "";
    public string NodeId { get; init; } = "local";
    public string OwnerId { get; init; } = "";
    public string ServicePrefix { get; init; } = "svc";
    public string BindAddress { get; init; } = "127.0.0.1";
    public int HttpPort { get; init; } = 80;
    public int HttpsPort { get; init; } = 443;
    public int RegistrationPort { get; init; } = 9443;
    public int ManagementPort { get; init; } = 9444;
    public bool DiscoveryEnabled { get; init; } = true;
    public IReadOnlyList<string> EndpointAddresses { get; init; } = ["127.0.0.1"];
    public int MinimumPort { get; init; } = 1024;
    public int MaximumPort { get; init; } = 65535;
    public string DnsServerAddress { get; init; } = "";
    public int DnsServerPort { get; init; } = 53;
    public DnsPublicationSettings DnsPublication { get; init; } = new();
    public RegistrationSettings Registration { get; init; } = new();
    public ServingPlanSettings ServingPlan { get; init; } = new();
    public GatewayPlanSettings GatewayPlan { get; init; } = new();

    public void Validate()
    {
        if (SchemaVersion != 1 || !Path.IsPathFullyQualified(DestinationDirectory) ||
            !string.Equals(Path.GetFullPath(DestinationDirectory), DestinationDirectory, StringComparison.Ordinal))
            throw new InvalidDataException("Unsupported site initialization version or destination.");
        RegistrationSiteTrust.RequireLabel(SiteId);
        RegistrationSiteTrust.RequireLabel(NodeId);
        RegistrationSiteTrust.RequireLabel(OwnerId);
        RegistrationSiteTrust.RequireLabel(ServicePrefix);
        var bundle = SiteInitializationBundle.Create(this, new string('A', 64));
        bundle.Application.Validate(); bundle.Gateway.Validate(); bundle.Enrollment.Validate();
        _ = new Mk8.Drava.Application.BLL.Registry.NodeGrant(NodeId, OwnerId, new string('A', 64), ServicePrefix,
            EndpointAddresses, MinimumPort, MaximumPort, DateTimeOffset.MaxValue, revoked: false);
    }
}

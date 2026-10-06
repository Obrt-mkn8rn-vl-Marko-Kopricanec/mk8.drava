using System.Net;

namespace Mk8.Drava.Configuration;

public sealed record NodeAgentBootstrap
{
    public uint SchemaVersion { get; init; } = 1;
    public RegistrationSiteTrust Site { get; init; } = new();
    public string NodeId { get; init; } = "";
    public string OwnerId { get; init; } = "";
    public string ServicePrefix { get; init; } = "";
    public string StateDirectory { get; init; } = "";
    public IpcEndpoint LocalListen { get; init; } = new();
    public string RelayAddress { get; init; } = "";
    public int RelayPort { get; init; }
    public IReadOnlyList<string> EndpointAddresses { get; init; } = [];
    public int MinimumPort { get; init; } = 1024;
    public int MaximumPort { get; init; } = 65535;
    public RelayLimits Relay { get; init; } = new();

    public void Validate()
    {
        if (SchemaVersion != 1) throw new InvalidDataException("Unsupported node-agent schema.");
        Site.Validate(); LocalListen.Validate(); Relay.Validate();
        foreach (var label in new[] { NodeId, OwnerId, ServicePrefix }) RegistrationSiteTrust.RequireLabel(label);
        if (!Path.IsPathFullyQualified(StateDirectory) || LocalListen.HttpsAddress.Length != 0)
            throw new InvalidDataException("Node SDK mappings require a private local socket or current-user pipe.");
        if (EndpointAddresses.Count is < 1 or > 64 || MinimumPort < 1 || MaximumPort > 65535 || MinimumPort > MaximumPort || RelayPort is < 0 or > 65535 ||
            RelayPort > 0 && (RelayPort < MinimumPort || RelayPort > MaximumPort))
            throw new InvalidDataException("Invalid node-agent admission or endpoint scope.");
        foreach (var address in EndpointAddresses) RequireAddress(address);
        RequireAddress(RelayAddress);
        if (!EndpointAddresses.Contains(RelayAddress, StringComparer.Ordinal)) throw new InvalidDataException("Relay listener must belong to the enrolled endpoint scope.");
    }

    internal static void RequireAddress(string value)
    {
        if (!IPAddress.TryParse(value, out var address) || !string.Equals(address.ToString(), value, StringComparison.Ordinal) || address.IsIPv4MappedToIPv6 ||
            address.IsIPv6Multicast || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) ||
            address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 && address.ScopeId != 0)
            throw new InvalidDataException("Node-agent requires canonical unscoped unicast literals.");
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && address.GetAddressBytes()[0] is 0 or >= 224)
            throw new InvalidDataException("Node-agent requires unicast IPv4.");
    }
}

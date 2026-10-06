using System.Net;
using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.Registry;

public sealed record NodeGrant
{
    public NodeGrant(string nodeId, string ownerId, string certificateFingerprint, string servicePrefix,
        IReadOnlyList<string> endpointAddresses, int minimumPort, int maximumPort, DateTimeOffset notAfterUtc, bool revoked)
    {
        RegistryNames.RequireLabel(nodeId);
        RegistryNames.RequireLabel(ownerId);
        RegistryNames.RequireLabel(servicePrefix);
        RegistryNames.RequireFingerprint(certificateFingerprint);
        ArgumentNullException.ThrowIfNull(endpointAddresses);
        if (endpointAddresses.Count is < 1 or > 64 || minimumPort < 1 || maximumPort > 65535 || minimumPort > maximumPort)
            throw new InvalidDataException("Invalid enrollment endpoint scope.");
        foreach (var address in endpointAddresses)
            if (!IPAddress.TryParse(address, out var parsed) || !string.Equals(parsed.ToString(), address, StringComparison.Ordinal) ||
                parsed.Equals(IPAddress.Any) || parsed.Equals(IPAddress.IPv6Any) || parsed.IsIPv4MappedToIPv6 || parsed.IsIPv6Multicast)
                throw new InvalidDataException("Enrollment requires canonical unicast endpoint literals.");
            else if (parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && parsed.GetAddressBytes()[0] is 0 or >= 224)
                throw new InvalidDataException("Enrollment cannot authorize unspecified, multicast or reserved IPv4 endpoints.");
        NodeId = nodeId;
        OwnerId = ownerId;
        CertificateFingerprint = certificateFingerprint;
        ServicePrefix = servicePrefix;
        EndpointAddresses = RuntimeList.Copy(endpointAddresses);
        MinimumPort = minimumPort;
        MaximumPort = maximumPort;
        NotAfterUtc = notAfterUtc;
        Revoked = revoked;
    }

    public string NodeId { get; }
    public string OwnerId { get; }
    public string CertificateFingerprint { get; }
    public string ServicePrefix { get; }
    public IReadOnlyList<string> EndpointAddresses { get; }
    public int MinimumPort { get; }
    public int MaximumPort { get; }
    public DateTimeOffset NotAfterUtc { get; }
    public bool Revoked { get; }

    public bool Authorizes(InstanceIntent intent, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (Revoked || nowUtc >= NotAfterUtc || !string.Equals(NodeId, intent.Identity.NodeId, StringComparison.Ordinal) ||
            !string.Equals(OwnerId, intent.Identity.OwnerId, StringComparison.Ordinal) ||
            !(string.Equals(intent.Identity.ServiceId, ServicePrefix, StringComparison.Ordinal) || intent.Identity.ServiceId.StartsWith(ServicePrefix + "-", StringComparison.Ordinal)) ||
            intent.Port < MinimumPort || intent.Port > MaximumPort) return false;
        if (intent.Relay is { } relay && (!string.Equals(relay.CertificateFingerprint, CertificateFingerprint, StringComparison.Ordinal) ||
            relay.Port < MinimumPort || relay.Port > MaximumPort || !EndpointAddresses.Contains(relay.Address, StringComparer.Ordinal))) return false;
        foreach (var address in EndpointAddresses)
            if (string.Equals(address, intent.Address, StringComparison.Ordinal)) return true;
        return false;
    }

    public NodeGrant Revoke() => new(NodeId, OwnerId, CertificateFingerprint, ServicePrefix, EndpointAddresses, MinimumPort, MaximumPort, NotAfterUtc, revoked: true);
}

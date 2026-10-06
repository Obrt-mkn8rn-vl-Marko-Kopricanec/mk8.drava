using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mk8.Drava.Configuration;
using Mk8.Drava.Contracts.Relay.V1;

namespace Mk8.Drava.Transport.Relay;

public static class RelayCapabilityJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true, MaxDepth = 8,
    };

    public static byte[] Encode(RelayCapability capability)
    {
        Validate(capability);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(capability, Options);
        if (bytes.Length > RelayCapabilityProof.MaximumPayloadBytes) throw new InvalidDataException("Relay capability exceeds its payload bound.");
        return bytes;
    }

    public static RelayCapability Decode(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length is < 1 or > RelayCapabilityProof.MaximumPayloadBytes) throw new InvalidDataException("Relay capability payload is invalid.");
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
        RequireUniqueProperties(document.RootElement);
        var capability = document.RootElement.Deserialize<RelayCapability>(Options) ?? throw new InvalidDataException("Relay capability is missing.");
        Validate(capability);
        return capability;
    }

    public static void Validate(RelayCapability capability)
    {
        ArgumentNullException.ThrowIfNull(capability);
        var identity = capability.Identity ?? throw new InvalidDataException("Relay identity is missing.");
        var advertisement = capability.Advertisement ?? throw new InvalidDataException("Relay endpoint is missing.");
        if (capability.Version != 1 || capability.Purpose is not (RelayPurpose.Exchange or RelayPurpose.Readiness)) throw new InvalidDataException("Unknown relay version or purpose.");
        foreach (var label in new[] { identity.SiteId, identity.NodeId, identity.OwnerId, identity.ServiceId, identity.ContractId, advertisement.DeploymentId, advertisement.Zone }) RegistrationSiteTrust.RequireLabel(label);
        foreach (var epoch in new[] { identity.InstanceId, identity.BootId, capability.ExchangeId, capability.CapabilityId, capability.ControllerEpoch, capability.AgentBootId }) RequireEpoch(epoch);
        if (!IPAddress.TryParse(advertisement.Address, out var address) || !string.Equals(address.ToString(), advertisement.Address, StringComparison.Ordinal) ||
            address.IsIPv4MappedToIPv6 || address.IsIPv6Multicast || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) throw new InvalidDataException("Relay backend requires a canonical unicast literal.");
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && address.GetAddressBytes()[0] is 0 or >= 224) throw new InvalidDataException("Relay backend requires unicast IPv4.");
        if (advertisement.Port is < 1 or > 65535 || advertisement.Weight is < 1 or > 100_000 || advertisement.Protocol is not ("http1" or "http2") ||
            advertisement.Scheme is not ("http" or "https") || string.Equals(advertisement.Protocol, "http2", StringComparison.Ordinal) && !string.Equals(advertisement.Scheme, "https", StringComparison.Ordinal)) throw new InvalidDataException("Unsupported relay endpoint.");
        RequireReadinessPath(advertisement.ReadinessPath);
        if (capability.TlsServerName.Length > 253 || capability.TlsServerName.Any(static value => value <= ' ' || value > '~' || value is '/' or '\\' or '@' or ',')) throw new InvalidDataException("Invalid relay TLS identity.");
        if (capability.IssuedAtUnixMilliseconds < 0 || capability.ExpiresAtUnixMilliseconds <= capability.IssuedAtUnixMilliseconds ||
            capability.ExpiresAtUnixMilliseconds - capability.IssuedAtUnixMilliseconds > 15_000 || capability.MaximumBytesPerDirection is < 1 or > 4L * 1024 * 1024 * 1024 ||
            capability.MaximumDurationSeconds is < 1 or > 3600) throw new InvalidDataException("Invalid relay capability limits.");
        if (capability.Purpose == RelayPurpose.Readiness && (capability.MaximumBytesPerDirection > 1024 * 1024 || capability.MaximumDurationSeconds > 5)) throw new InvalidDataException("Readiness relay exceeds its limited purpose.");
    }

    private static void RequireEpoch(string value)
    {
        if (!Guid.TryParseExact(value, "N", out var epoch) || epoch == Guid.Empty || !string.Equals(epoch.ToString("N"), value, StringComparison.Ordinal)) throw new InvalidDataException("Relay epoch is not canonical.");
    }

    private static void RequireReadinessPath(string path)
    {
        if (path.Length is < 1 or > 1024 || !path.StartsWith('/') || path.StartsWith("//", StringComparison.Ordinal)) throw new InvalidDataException("Relay requires a declared readiness origin.");
        foreach (var value in path)
            if (value <= ' ' || value > '~' || value is '\\' or '#') throw new InvalidDataException("Invalid relay readiness origin.");
    }

    private static void RequireUniqueProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate relay property.");
                RequireUniqueProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array) throw new InvalidDataException("Relay capability does not accept collection properties.");
    }
}

using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Mk8.Drava.Contracts.Registration.V1;

namespace Mk8.Drava.Registration;

internal static class BoundServiceAdvertisement
{
    public static ServiceAdvertisement Read(IServer server, DravaRegistrationOptions options, IPAddress? outboundAddress)
    {
        var endpoints = server.Features.Get<IServerAddressesFeature>() ?? throw new InvalidDataException("Registration requires an actual bound server endpoint.");
        foreach (var value in endpoints.Addresses)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var bound) || bound.Port is < 1 or > 65535 || bound.Scheme is not "http" and not "https") continue;
            if (string.Equals(options.UpstreamProtocol, "http2", StringComparison.Ordinal) && !string.Equals(bound.Scheme, "https", StringComparison.Ordinal)) continue;
            var address = ResolveAddress(bound, options.AdvertiseAddress, outboundAddress);
            return new ServiceAdvertisement
            {
                DeploymentId = options.DeploymentId, Address = address.ToString(), Port = bound.Port, Scheme = bound.Scheme,
                Protocol = options.UpstreamProtocol, ReadinessPath = options.ReadinessPath, Zone = options.Zone, Weight = options.Weight,
            };
        }
        throw new InvalidDataException("No actual bound server endpoint matches the registered upstream protocol.");
    }

    private static IPAddress ResolveAddress(Uri bound, string advertised, IPAddress? outbound)
    {
        var literal = IPAddress.TryParse(bound.Host.Trim('[', ']'), out var parsed) ? parsed : bound.IsLoopback ? IPAddress.Loopback : throw new InvalidDataException("Bound service requires a literal address.");
        var wildcard = literal.Equals(IPAddress.Any) || literal.Equals(IPAddress.IPv6Any);
        if (advertised.Length > 0)
        {
            var requested = IPAddress.Parse(advertised);
            if (!wildcard && !requested.Equals(literal)) throw new InvalidDataException("Advertised address differs from the actual bound service.");
            return requested;
        }
        return wildcard ? outbound ?? throw new InvalidDataException("Wildcard service binding requires a verified outbound interface address.") : literal;
    }
}

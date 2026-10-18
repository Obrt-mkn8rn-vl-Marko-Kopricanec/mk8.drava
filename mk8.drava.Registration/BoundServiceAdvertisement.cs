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
            if (!Uri.TryCreate(value, UriKind.Absolute, out var bound) || bound.Port is < 1 or > 65535 || bound.Scheme is not "http" and not "https" ||
                bound.UserInfo.Length != 0 || !string.Equals(bound.AbsolutePath, "/", StringComparison.Ordinal) || bound.Query.Length != 0 || bound.Fragment.Length != 0)
            {
                continue;
            }
            if (string.Equals(options.UpstreamProtocol, "http2", StringComparison.Ordinal) && !string.Equals(bound.Scheme, "https", StringComparison.Ordinal)) continue;
            var address = ResolveAddress(bound, options.AdvertiseAddress, outboundAddress);
            if (address is null) continue;
            return new ServiceAdvertisement
            {
                DeploymentId = options.DeploymentId, Address = address.ToString(), Port = bound.Port, Scheme = bound.Scheme,
                Protocol = options.UpstreamProtocol, ReadinessPath = options.ReadinessPath, Zone = options.Zone, Weight = options.Weight,
            };
        }
        throw new InvalidDataException("No actual bound server endpoint matches the registered upstream protocol.");
    }

    private static IPAddress? ResolveAddress(Uri bound, string advertised, IPAddress? outbound)
    {
        var literal = IPAddress.TryParse(bound.Host.Trim('[', ']'), out var parsed) ? parsed : bound.IsLoopback ? IPAddress.Loopback : null;
        if (literal is null) return null;
        var wildcard = literal.Equals(IPAddress.Any) || literal.Equals(IPAddress.IPv6Any);
        if (advertised.Length > 0)
        {
            var requested = IPAddress.Parse(advertised);
            return Matches(literal, requested, wildcard) ? requested : null;
        }
        return wildcard ? outbound is not null && Matches(literal, outbound, wildcard) ? outbound : null : literal;
    }

    // The addresses feature does not prove that an IPv6 wildcard socket accepts IPv4.
    private static bool Matches(IPAddress bound, IPAddress requested, bool wildcard) =>
        !requested.Equals(IPAddress.Any) && !requested.Equals(IPAddress.IPv6Any) && !requested.IsIPv4MappedToIPv6 &&
        bound.AddressFamily == requested.AddressFamily && (wildcard || requested.Equals(bound));
}

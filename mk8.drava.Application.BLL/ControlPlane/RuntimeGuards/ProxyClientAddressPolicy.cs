using System.Net;
using Mk8.Drava.Application.BLL.ControlPlane.Headers;

namespace Mk8.Drava.Application.BLL.ControlPlane.RuntimeGuards;
public static class ProxyClientAddressPolicy
{
    public static string? NormalizeClientIp(IPAddress? address)
    {
        return address is null ? null : NormalizeRequiredClientIp(address);
    }

    public static string NormalizeRequiredClientIp(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4().ToString() : address.ToString();
    }

    public static ForwardedHeadersPeer ToForwardedHeadersPeer(IPEndPoint? remoteEndPoint)
    {
        var address = NormalizeClientIp(remoteEndPoint?.Address);
        return new ForwardedHeadersPeer(address, address ?? remoteEndPoint?.ToString());
    }
}

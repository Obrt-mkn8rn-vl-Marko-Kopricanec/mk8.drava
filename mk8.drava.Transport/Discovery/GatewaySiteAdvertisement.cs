using System.Net;
using Haukcode.Mdns;
using Mk8.Drava.Configuration;

namespace Mk8.Drava.Transport.Discovery;

public sealed class GatewaySiteAdvertisement : IAsyncDisposable
{
    private readonly MdnsAdvertiser _advertiser;
    public GatewaySiteAdvertisement(GatewayBootstrap bootstrap)
    {
        ArgumentNullException.ThrowIfNull(bootstrap);
        var boot = Guid.NewGuid().ToString("N");
        var profile = new ServiceProfile("drava-" + boot, "_mk8-drava._tcp", checked((ushort)bootstrap.RegistrationPort),
            new Dictionary<string, string>(StringComparer.Ordinal) { ["v"] = "1", ["site"] = bootstrap.SiteId, ["gateway"] = bootstrap.GatewayId, ["boot"] = boot, ["root"] = bootstrap.EnrollmentRootFingerprint });
        var address = IPAddress.Parse(bootstrap.BindAddress);
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            throw new NotSupportedException("Automatic discovery requires an IPv4 Gateway listener; IPv6 locators can be configured explicitly.");
        _advertiser = new MdnsAdvertiser(profile, address.Equals(IPAddress.Any) ? null : address);
        try { _advertiser.Start(); }
        catch { _advertiser.Dispose(); throw; }
    }
    public ValueTask DisposeAsync() => _advertiser.DisposeAsync();
}

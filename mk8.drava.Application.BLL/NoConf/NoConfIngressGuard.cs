using System.Net;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.Registry;

namespace Mk8.Drava.Application.BLL.NoConf;

public sealed class NoConfIngressGuard
{
    private readonly string _localNodeId;
    private readonly IReadOnlyList<string> _addresses;
    private readonly IReadOnlyList<int> _ports;
    private readonly bool _wildcard;

    public NoConfIngressGuard(string localNodeId, IReadOnlyList<string> addresses, IReadOnlyList<int> ports)
    {
        RegistryNames.RequireLabel(localNodeId);
        ArgumentNullException.ThrowIfNull(addresses);
        ArgumentNullException.ThrowIfNull(ports);
        _localNodeId = localNodeId;
        _addresses = RuntimeList.Copy(addresses);
        _ports = RuntimeList.Copy(ports);
        foreach (var address in addresses)
            if (IPAddress.Parse(address).Equals(IPAddress.Any) || IPAddress.Parse(address).Equals(IPAddress.IPv6Any)) _wildcard = true;
    }

    public void Validate(InstanceIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        var protectedPort = false;
        foreach (var port in _ports) if (port == intent.Port) protectedPort = true;
        if (!protectedPort) return;
        var local = string.Equals(intent.Identity.NodeId, _localNodeId, StringComparison.Ordinal);
        if (!local && IPAddress.IsLoopback(IPAddress.Parse(intent.Address))) return;
        var protectedAddress = local && _wildcard;
        foreach (var address in _addresses)
            if (string.Equals(address, intent.Address, StringComparison.Ordinal)) protectedAddress = true;
        if (protectedAddress) throw new InvalidDataException("A service cannot register a protected Gateway or registration endpoint.");
    }
}

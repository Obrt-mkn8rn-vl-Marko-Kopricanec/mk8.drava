using System.Net;
using Mk8.Drava.Application.BLL.ControlPlane.RuntimeGuards;

namespace Mk8.Drava.Application.INF.Proxy.RuntimeGuards;
public sealed class ProxyClientAddressSyntaxPolicy : IProxyClientAddressSyntaxPolicy
{
    public bool IsIpLiteral(string value)
    {
        return IPAddress.TryParse(value, out _);
    }
}

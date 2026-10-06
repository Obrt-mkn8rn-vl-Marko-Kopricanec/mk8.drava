using System.Net.Security;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Listeners;
using Mk8.Drava.Application.BLL.Administration.ContractMapping;

namespace Mk8.Drava.CompatibilityTests.LegacyIngress.Proxy.Tls;
public static class ListenerProtocolAdvertisement
{
    public static List<SslApplicationProtocol> BuildTcpAlpn(RuntimeListenerProtocols protocols)
    {
        return ListenerProtocolAdvertisementPolicy.BuildTcpAlpnProtocolNames(ListenerProtocolAdvertisementInputMapper.FromTcpRuntimeProtocols(protocols)).Select(static protocol => new SslApplicationProtocol(protocol)).ToList();
    }

    public static List<SslApplicationProtocol> BuildHttp3Alpn(RuntimeListener listener)
    {
        return ListenerProtocolAdvertisementPolicy.BuildHttp3AlpnProtocolNames(ListenerProtocolAdvertisementInputMapper.FromHttp3RuntimeListener(listener)).Select(static protocol => new SslApplicationProtocol(protocol)).ToList();
    }
}

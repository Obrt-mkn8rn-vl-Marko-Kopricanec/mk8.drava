using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.Listeners;
public static class ListenerProtocolAdvertisementPolicy
{
    public const string Http1Alpn = "http/1.1";
    public const string Http2Alpn = "h2";
    public const string Http3Alpn = "h3";
    public static IReadOnlyList<string> BuildTcpAlpnProtocolNames(TcpAlpnAdvertisementInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        List<string> advertised = [];
        if (input.Http2Enabled)
        {
            advertised.Add(Http2Alpn);
        }

        if (input.Http1Enabled)
        {
            advertised.Add(Http1Alpn);
        }

        return advertised;
    }

    public static IReadOnlyList<string> BuildHttp3AlpnProtocolNames(Http3AlpnAdvertisementInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input.EnabledForTraffic ? [Http3Alpn] : [];
    }
}

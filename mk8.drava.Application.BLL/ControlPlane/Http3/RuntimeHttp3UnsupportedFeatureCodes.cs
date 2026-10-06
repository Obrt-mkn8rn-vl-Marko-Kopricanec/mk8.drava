namespace Mk8.Drava.Application.BLL.ControlPlane.Http3;
public static class RuntimeHttp3UnsupportedFeatureCodes
{
    public static IReadOnlyList<string> EffectiveConfig { get; } = ["h3c", "connect_over_http3", "websocket_over_http3", "connect_udp_over_http3", "masque", "webtransport_over_http3"];
    public static IReadOnlyList<string> StatusSummary { get; } = ["h3c", "connect", "websocket", "connect-udp", "masque", "webtransport"];
}

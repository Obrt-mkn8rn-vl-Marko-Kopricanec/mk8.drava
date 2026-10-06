namespace Mk8.Drava.Application.BLL.Configuration;
public static class RuntimeUpstreamProtocol
{
    public const string Http1 = "http1";
    public const string Http2 = "http2";
    public const string Http3 = "http3";
    public static bool IsHttp2(string protocol)
    {
        return string.Equals(protocol, Http2, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsHttp3(string protocol)
    {
        return string.Equals(protocol, Http3, StringComparison.OrdinalIgnoreCase);
    }
}

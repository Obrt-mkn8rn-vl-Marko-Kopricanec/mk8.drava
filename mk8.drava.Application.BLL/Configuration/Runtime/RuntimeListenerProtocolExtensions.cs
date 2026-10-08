namespace Mk8.Drava.Application.BLL.Configuration;
public static class RuntimeListenerProtocolExtensions
{
    public static readonly IReadOnlyList<string> SupportedConfigValues = RuntimeHttp3Compatibility.SupportedProtocolConfigValues;
    public static bool HasTcpProtocols(this RuntimeListenerProtocols protocols)
    {
        return (protocols & (RuntimeListenerProtocols.Http1 | RuntimeListenerProtocols.Http2)) != RuntimeListenerProtocols.None;
    }

    public static bool HasHttp3(this RuntimeListenerProtocols protocols)
    {
        return protocols.HasFlag(RuntimeListenerProtocols.Http3);
    }

    public static RuntimeListenerProtocolParseResult ParseConfigText(string? protocols)
    {
        return RuntimeHttp3Compatibility.ParseProtocols(protocols);
    }

    public static RuntimeListenerProtocols ParseConfigTextOrDefault(string? protocols)
    {
        return RuntimeHttp3Compatibility.ParseProtocolsOrDefault(protocols);
    }

    public static string ToConfigText(this RuntimeListenerProtocols protocols)
    {
        return protocols switch
        {
            RuntimeListenerProtocols.Http2 => "http2",
            RuntimeListenerProtocols.Http1AndHttp2 => "http1AndHttp2",
            RuntimeListenerProtocols.Http3 => "http3",
            RuntimeListenerProtocols.Http1AndHttp3 => "http1AndHttp3",
            RuntimeListenerProtocols.Http2AndHttp3 => "http2AndHttp3",
            RuntimeListenerProtocols.Http1AndHttp2AndHttp3 => "http1AndHttp2AndHttp3",
            _ => "http1"
        };
    }
}

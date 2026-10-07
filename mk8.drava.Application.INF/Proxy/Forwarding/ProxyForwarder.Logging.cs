namespace Mk8.Drava.Application.INF.Proxy.Forwarding;

public sealed partial class ProxyForwarder
{
    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10013, Level = global::Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Proxied {Method} {Target} to upstream {UpstreamName}", SkipEnabledCheck = true)]
    private static partial void LogProxiedToUpstream10013(global::Microsoft.Extensions.Logging.ILogger logger, string method, string target, string upstreamName, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10014, Level = global::Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Rejected oversized request body for {Method} {Target}", SkipEnabledCheck = true)]
    private static partial void LogRejectedOversizedRequestBodyFor10014(global::Microsoft.Extensions.Logging.ILogger logger, string method, string target, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10015, Level = global::Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Rejected malformed request body for {Method} {Target}", SkipEnabledCheck = true)]
    private static partial void LogRejectedMalformedRequestBodyFor10015(global::Microsoft.Extensions.Logging.ILogger logger, string method, string target, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10016, Level = global::Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Upstream response framing failed for {Method} {Target} to upstream {UpstreamName}", SkipEnabledCheck = true)]
    private static partial void LogUpstreamResponseFramingFailedFor10016(global::Microsoft.Extensions.Logging.ILogger logger, string method, string target, string upstreamName, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10017, Level = global::Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Upstream HTTP/2 response framing failed for {Method} {Target} to upstream {UpstreamName}", SkipEnabledCheck = true)]
    private static partial void LogUpstreamHTTPResponseFramingFailed10017(global::Microsoft.Extensions.Logging.ILogger logger, string method, string target, string upstreamName, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10018, Level = global::Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Upstream HTTP/3 forwarding failed for {Method} {Target} to upstream {UpstreamName}", SkipEnabledCheck = true)]
    private static partial void LogUpstreamHTTPForwardingFailedFor10018(global::Microsoft.Extensions.Logging.ILogger logger, string method, string target, string upstreamName, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10019, Level = global::Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Upstream TLS failed for {Method} {Target} to upstream {UpstreamName}", SkipEnabledCheck = true)]
    private static partial void LogUpstreamTLSFailedForTo10019(global::Microsoft.Extensions.Logging.ILogger logger, string method, string target, string upstreamName, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10020, Level = global::Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Upstream forwarding failed for {Method} {Target} to upstream {UpstreamName}", SkipEnabledCheck = true)]
    private static partial void LogUpstreamForwardingFailedForTo10020(global::Microsoft.Extensions.Logging.ILogger logger, string method, string target, string upstreamName, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10021, Level = global::Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Client request body timed out for {Method} {Target}", SkipEnabledCheck = true)]
    private static partial void LogClientRequestBodyTimedOut10021(global::Microsoft.Extensions.Logging.ILogger logger, string method, string target, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10022, Level = global::Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Timed out connecting to upstream {UpstreamName}", SkipEnabledCheck = true)]
    private static partial void LogTimedOutConnectingToUpstream10022(global::Microsoft.Extensions.Logging.ILogger logger, string upstreamName, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10023, Level = global::Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Timed out waiting for upstream response head from {UpstreamName}", SkipEnabledCheck = true)]
    private static partial void LogTimedOutWaitingForUpstream10023(global::Microsoft.Extensions.Logging.ILogger logger, string upstreamName, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10024, Level = global::Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Timed out relaying upstream response body from {UpstreamName}", SkipEnabledCheck = true)]
    private static partial void LogTimedOutRelayingUpstreamResponse10024(global::Microsoft.Extensions.Logging.ILogger logger, string upstreamName, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10025, Level = global::Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Downstream write timed out for {Method} {Target}", SkipEnabledCheck = true)]
    private static partial void LogDownstreamWriteTimedOutFor10025(global::Microsoft.Extensions.Logging.ILogger logger, string method, string target, global::System.Exception? exception);
}

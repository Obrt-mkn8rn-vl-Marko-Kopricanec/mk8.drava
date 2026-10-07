namespace Mk8.Drava.Application.INF.Proxy.Forwarding;

public sealed partial class UpgradeForwarder
{
    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10028, Level = global::Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Upgraded {Method} {Target} to protocol {Protocol} through upstream {UpstreamName}", SkipEnabledCheck = true)]
    private static partial void LogUpgradedToProtocolThroughUpstream10028(global::Microsoft.Extensions.Logging.ILogger logger, string method, string target, string protocol, string upstreamName, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10029, Level = global::Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Upstream Upgrade response failed for {Method} {Target} to upstream {UpstreamName}", SkipEnabledCheck = true)]
    private static partial void LogUpstreamUpgradeResponseFailedFor10029(global::Microsoft.Extensions.Logging.ILogger logger, string method, string target, string upstreamName, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10030, Level = global::Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Upgrade forwarding failed for {Method} {Target} to upstream {UpstreamName}", SkipEnabledCheck = true)]
    private static partial void LogUpgradeForwardingFailedForTo10030(global::Microsoft.Extensions.Logging.ILogger logger, string method, string target, string upstreamName, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10031, Level = global::Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Timed out connecting Upgrade request to upstream {UpstreamName}", SkipEnabledCheck = true)]
    private static partial void LogTimedOutConnectingUpgradeRequest10031(global::Microsoft.Extensions.Logging.ILogger logger, string upstreamName, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10032, Level = global::Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Timed out waiting for upstream Upgrade response head from {UpstreamName}", SkipEnabledCheck = true)]
    private static partial void LogTimedOutWaitingForUpstream10032(global::Microsoft.Extensions.Logging.ILogger logger, string upstreamName, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10033, Level = global::Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Timed out relaying non-101 upstream response body from {UpstreamName}", SkipEnabledCheck = true)]
    private static partial void LogTimedOutRelayingNonUpstream10033(global::Microsoft.Extensions.Logging.ILogger logger, string upstreamName, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10034, Level = global::Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Downstream write timed out for Upgrade {Method} {Target}", SkipEnabledCheck = true)]
    private static partial void LogDownstreamWriteTimedOutFor10034(global::Microsoft.Extensions.Logging.ILogger logger, string method, string target, global::System.Exception? exception);
}

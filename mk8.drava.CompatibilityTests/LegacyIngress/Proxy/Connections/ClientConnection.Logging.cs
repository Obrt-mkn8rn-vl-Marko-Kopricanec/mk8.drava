namespace Mk8.Drava.CompatibilityTests.LegacyIngress.Proxy.Connections;

internal sealed partial class ClientConnection
{
    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10035, Level = global::Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Rejected malformed request head with parse error {ParseError}", SkipEnabledCheck = true)]
    private static partial void LogRejectedMalformedRequestHeadWith10035(global::Microsoft.Extensions.Logging.ILogger logger, global::Mk8.Drava.Application.BLL.ControlPlane.Http1.Http1ParseError parseError, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10036, Level = global::Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Client timed out before sending a complete request head.", SkipEnabledCheck = true)]
    private static partial void LogClientTimedOutBeforeSending10036(global::Microsoft.Extensions.Logging.ILogger logger, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10037, Level = global::Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Client keep-alive idle timeout elapsed.", SkipEnabledCheck = true)]
    private static partial void LogClientKeepAliveIdleTimeout10037(global::Microsoft.Extensions.Logging.ILogger logger, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10038, Level = global::Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Timed out while writing a generated response to the client.", SkipEnabledCheck = true)]
    private static partial void LogTimedOutWhileWritingA10038(global::Microsoft.Extensions.Logging.ILogger logger, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10039, Level = global::Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Client disconnected during request processing.", SkipEnabledCheck = true)]
    private static partial void LogClientDisconnectedDuringRequestProcessing10039(global::Microsoft.Extensions.Logging.ILogger logger, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10040, Level = global::Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Rejected Upgrade request for {Method} {Target}: {RejectionReason}", SkipEnabledCheck = true)]
    private static partial void LogRejectedUpgradeRequestFor10040(global::Microsoft.Extensions.Logging.ILogger logger, string method, string target, string rejectionReason, global::System.Exception? exception);
}

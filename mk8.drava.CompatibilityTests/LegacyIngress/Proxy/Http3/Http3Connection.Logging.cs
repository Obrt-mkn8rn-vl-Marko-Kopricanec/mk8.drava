namespace Mk8.Drava.CompatibilityTests.LegacyIngress.Proxy.Http3;

internal sealed partial class Http3Connection
{
    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10055, Level = global::Microsoft.Extensions.Logging.LogLevel.Debug, Message = "HTTP/3 QUIC connection ended.", SkipEnabledCheck = true)]
    private static partial void LogHTTPQUICConnectionEnded10055(global::Microsoft.Extensions.Logging.ILogger logger, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10056, Level = global::Microsoft.Extensions.Logging.LogLevel.Debug, Message = "HTTP/3 connection ended with I/O failure.", SkipEnabledCheck = true)]
    private static partial void LogHTTPConnectionEndedWithI10056(global::Microsoft.Extensions.Logging.ILogger logger, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10057, Level = global::Microsoft.Extensions.Logging.LogLevel.Debug, Message = "HTTP/3 stream ended with I/O failure.", SkipEnabledCheck = true)]
    private static partial void LogHTTPStreamEndedWithI10057(global::Microsoft.Extensions.Logging.ILogger logger, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10058, Level = global::Microsoft.Extensions.Logging.LogLevel.Debug, Message = "HTTP/3 failed to send SETTINGS.", SkipEnabledCheck = true)]
    private static partial void LogHTTPFailedToSendSETTINGS10058(global::Microsoft.Extensions.Logging.ILogger logger, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10059, Level = global::Microsoft.Extensions.Logging.LogLevel.Debug, Message = "HTTP/3 unidirectional stream ended.", SkipEnabledCheck = true)]
    private static partial void LogHTTPUnidirectionalStreamEnded10059(global::Microsoft.Extensions.Logging.ILogger logger, global::System.Exception? exception);
}

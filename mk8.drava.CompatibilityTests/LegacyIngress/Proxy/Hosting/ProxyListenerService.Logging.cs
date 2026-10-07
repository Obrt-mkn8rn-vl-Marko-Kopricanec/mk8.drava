namespace Mk8.Drava.CompatibilityTests.LegacyIngress.Proxy.Hosting;

public sealed partial class ProxyListenerService
{
    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10041, Level = global::Microsoft.Extensions.Logging.LogLevel.Information, Message = "Proxy listener {ListenerName} prepared on {Address}:{Port}", SkipEnabledCheck = true)]
    private static partial void LogProxyListenerPreparedOn10041(global::Microsoft.Extensions.Logging.ILogger logger, string listenerName, string address, int port, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10042, Level = global::Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Proxy listener reload failed while preparing new listeners.", SkipEnabledCheck = true)]
    private static partial void LogProxyListenerReloadFailedWhile10042(global::Microsoft.Extensions.Logging.ILogger logger, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10043, Level = global::Microsoft.Extensions.Logging.LogLevel.Information, Message = "HTTP/3 QUIC listener {ListenerName} prepared on {Address}:{Port}", SkipEnabledCheck = true)]
    private static partial void LogHTTPQUICListenerPreparedOn10043(global::Microsoft.Extensions.Logging.ILogger logger, string listenerName, string address, int port, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10044, Level = global::Microsoft.Extensions.Logging.LogLevel.Warning, Message = "HTTP/3 QUIC listener {ListenerName} failed to start.", SkipEnabledCheck = true)]
    private static partial void LogHTTPQUICListenerFailedTo10044(global::Microsoft.Extensions.Logging.ILogger logger, string listenerName, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10045, Level = global::Microsoft.Extensions.Logging.LogLevel.Information, Message = "Proxy listener reload applied: added={Added} removed={Removed} changed={Changed} unchanged={Unchanged}", SkipEnabledCheck = true)]
    private static partial void LogProxyListenerReloadAppliedAdded10045(global::Microsoft.Extensions.Logging.ILogger logger, int added, int removed, int changed, int unchanged, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10046, Level = global::Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Proxy listener {ListenerName} stopped after socket failure.", SkipEnabledCheck = true)]
    private static partial void LogProxyListenerStoppedAfterSocket10046(global::Microsoft.Extensions.Logging.ILogger logger, string listenerName, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10047, Level = global::Microsoft.Extensions.Logging.LogLevel.Error, Message = "Proxy listener {ListenerName} stopped unexpectedly.", SkipEnabledCheck = true)]
    private static partial void LogProxyListenerStoppedUnexpectedly10047(global::Microsoft.Extensions.Logging.ILogger logger, string listenerName, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10048, Level = global::Microsoft.Extensions.Logging.LogLevel.Warning, Message = "HTTP/3 QUIC listener {ListenerName} stopped after transport failure.", SkipEnabledCheck = true)]
    private static partial void LogHTTPQUICListenerStoppedAfter10048(global::Microsoft.Extensions.Logging.ILogger logger, string listenerName, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10049, Level = global::Microsoft.Extensions.Logging.LogLevel.Error, Message = "HTTP/3 QUIC listener {ListenerName} stopped unexpectedly.", SkipEnabledCheck = true)]
    private static partial void LogHTTPQUICListenerStoppedUnexpectedly10049(global::Microsoft.Extensions.Logging.ILogger logger, string listenerName, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10050, Level = global::Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Client connection ended with an I/O error.", SkipEnabledCheck = true)]
    private static partial void LogClientConnectionEndedWithAn10050(global::Microsoft.Extensions.Logging.ILogger logger, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10051, Level = global::Microsoft.Extensions.Logging.LogLevel.Error, Message = "Client connection failed unexpectedly.", SkipEnabledCheck = true)]
    private static partial void LogClientConnectionFailedUnexpectedly10051(global::Microsoft.Extensions.Logging.ILogger logger, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10052, Level = global::Microsoft.Extensions.Logging.LogLevel.Debug, Message = "HTTP/3 client connection ended with an I/O error.", SkipEnabledCheck = true)]
    private static partial void LogHTTPClientConnectionEndedWith10052(global::Microsoft.Extensions.Logging.ILogger logger, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10053, Level = global::Microsoft.Extensions.Logging.LogLevel.Error, Message = "HTTP/3 client connection failed unexpectedly.", SkipEnabledCheck = true)]
    private static partial void LogHTTPClientConnectionFailedUnexpectedly10053(global::Microsoft.Extensions.Logging.ILogger logger, global::System.Exception? exception);
}

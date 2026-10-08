namespace Mk8.Drava.CompatibilityTests.LegacyIngress.Proxy.Tls;

internal sealed partial class TlsConnectionAuthenticator
{
    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10061, Level = global::Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Rejected TLS handshake for listener {ListenerName} because the concurrent handshake limit is exhausted.", SkipEnabledCheck = true)]
    private static partial void LogRejectedTLSHandshakeForListener10061(global::Microsoft.Extensions.Logging.ILogger logger, string listenerName, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10062, Level = global::Microsoft.Extensions.Logging.LogLevel.Debug, Message = "TLS handshake timed out for listener {ListenerName}.", SkipEnabledCheck = true)]
    private static partial void LogTLSHandshakeTimedOutFor10062(global::Microsoft.Extensions.Logging.ILogger logger, string listenerName, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10063, Level = global::Microsoft.Extensions.Logging.LogLevel.Debug, Message = "TLS handshake failed for listener {ListenerName}.", SkipEnabledCheck = true)]
    private static partial void LogTLSHandshakeFailedForListener10063(global::Microsoft.Extensions.Logging.ILogger logger, string listenerName, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10064, Level = global::Microsoft.Extensions.Logging.LogLevel.Debug, Message = "TLS handshake ended with I/O failure for listener {ListenerName}.", SkipEnabledCheck = true)]
    private static partial void LogTLSHandshakeEndedWithI10064(global::Microsoft.Extensions.Logging.ILogger logger, string listenerName, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10065, Level = global::Microsoft.Extensions.Logging.LogLevel.Error, Message = "TLS handshake failed unexpectedly for listener {ListenerName}.", SkipEnabledCheck = true)]
    private static partial void LogTLSHandshakeFailedUnexpectedlyFor10065(global::Microsoft.Extensions.Logging.ILogger logger, string listenerName, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10066, Level = global::Microsoft.Extensions.Logging.LogLevel.Debug, Message = "No TLS certificate matched SNI host {HostName} for listener {ListenerName}.", SkipEnabledCheck = true)]
    private static partial void LogNoTLSCertificateMatchedSNI10066(global::Microsoft.Extensions.Logging.ILogger logger, string hostName, string listenerName, global::System.Exception? exception);
}

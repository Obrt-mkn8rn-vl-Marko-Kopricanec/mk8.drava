namespace Mk8.Drava.CompatibilityTests.LegacyIngress.Proxy.Http3;

public sealed partial class SystemHttp3QuicListenerFactory
{
    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10060, Level = global::Microsoft.Extensions.Logging.LogLevel.Debug, Message = "No QUIC certificate matched SNI host {HostName} for listener {ListenerName}.", SkipEnabledCheck = true)]
    private static partial void LogNoQUICCertificateMatchedSNI10060(global::Microsoft.Extensions.Logging.ILogger logger, string hostName, string listenerName, global::System.Exception? exception);
}

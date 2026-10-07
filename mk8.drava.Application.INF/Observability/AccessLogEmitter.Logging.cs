namespace Mk8.Drava.Application.INF.Observability;

public sealed partial class AccessLogEmitter
{
    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10007, Level = global::Microsoft.Extensions.Logging.LogLevel.Information, Message = "Proxy access {RequestId} listener={ListenerName} transport={Transport} protocol={Protocol} client={ClientEndpoint} method={Method} host={Host} targetPath={TargetPath} route={RouteName} upstream={UpstreamName} upstreamEndpoint={UpstreamEndpoint} status={StatusCode} durationMs={DurationMilliseconds} failure={FailureKind} responseStarted={ResponseStarted} keepAlive={KeepAlive} upgrade={IsUpgrade} tunnel={TunnelEstablished} configVersion={ConfigVersion}", SkipEnabledCheck = true)]
    private static partial void LogProxyAccessListenerTransportProtocol10007(global::Microsoft.Extensions.Logging.ILogger logger, string requestId, string listenerName, string transport, string protocol, string? clientEndpoint, string? method, string? host, string? targetPath, string? routeName, string? upstreamName, string? upstreamEndpoint, int? statusCode, long durationMilliseconds, string failureKind, bool responseStarted, bool keepAlive, bool isUpgrade, bool tunnelEstablished, int configVersion, global::System.Exception? exception);
}

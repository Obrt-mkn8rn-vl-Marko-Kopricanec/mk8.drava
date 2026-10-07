namespace Mk8.Drava.Application.INF.Proxy.Forwarding;

public sealed partial class TunnelRelay
{
    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10026, Level = global::Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Upgraded tunnel idle timeout elapsed.", SkipEnabledCheck = true)]
    private static partial void LogUpgradedTunnelIdleTimeoutElapsed10026(global::Microsoft.Extensions.Logging.ILogger logger, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10027, Level = global::Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Upgraded tunnel relay ended with an I/O failure.", SkipEnabledCheck = true)]
    private static partial void LogUpgradedTunnelRelayEndedWith10027(global::Microsoft.Extensions.Logging.ILogger logger, global::System.Exception? exception);
}

namespace Mk8.Drava.Application.DAL.Observability;

public sealed partial class ProxyPersistentLogWriter
{
    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10002, Level = global::Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Failed to persist {LogName} log entry.", SkipEnabledCheck = true)]
    private static partial void LogFailedToPersistLogEntry10002(global::Microsoft.Extensions.Logging.ILogger logger, string logName, global::System.Exception? exception);
}

namespace Mk8.Drava.Application.INF.Observability;

public sealed partial class AdminAuthenticationLogger
{
    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10009, Level = global::Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Admin request arrived before an active proxy configuration snapshot was available.", SkipEnabledCheck = true)]
    private static partial void LogAdminRequestArrivedBeforeAn10009(global::Microsoft.Extensions.Logging.ILogger logger, global::System.Exception? exception);
}

namespace Mk8.Drava.Application.INF.Observability;

public sealed partial class ProxyConfigurationReloadLogger
{
    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10010, Level = global::Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Proxy configuration reload failed from {SourcePath}: {Errors}", SkipEnabledCheck = true)]
    private static partial void LogProxyConfigurationReloadFailedFrom10010(global::Microsoft.Extensions.Logging.ILogger logger, string sourcePath, string errors, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10011, Level = global::Microsoft.Extensions.Logging.LogLevel.Information, Message = "Proxy configuration version {Version} loaded from {SourcePath}", SkipEnabledCheck = true)]
    private static partial void LogProxyConfigurationVersionLoadedFrom10011(global::Microsoft.Extensions.Logging.ILogger logger, int version, string sourcePath, global::System.Exception? exception);
}

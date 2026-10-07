namespace Mk8.Drava.Application.DAL.Configuration.Loading;

public sealed partial class ProxyConfigurationLoader
{
    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10000, Level = global::Microsoft.Extensions.Logging.LogLevel.Warning, Message = "No proxy site configuration files were found in {SourcePath}; MDRAVA will start with no configured sites, listeners, or routes.", SkipEnabledCheck = true)]
    private static partial void LogNoProxySiteConfigurationFiles10000(global::Microsoft.Extensions.Logging.ILogger logger, string sourcePath, global::System.Exception? exception);

    [global::Microsoft.Extensions.Logging.LoggerMessage(EventId = 10001, Level = global::Microsoft.Extensions.Logging.LogLevel.Information, Message = "Proxy operational configuration file {ConfigPath} was not found; using in-memory default timeout settings.", SkipEnabledCheck = true)]
    private static partial void LogProxyOperationalConfigurationFileWas10001(global::Microsoft.Extensions.Logging.ILogger logger, string configPath, global::System.Exception? exception);
}

using Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
using Microsoft.Extensions.Logging;

namespace Mk8.Drava.Application.INF.Observability;
public sealed partial class ProxyConfigurationReloadLogger : IProxyConfigurationReloadEventSink
{
    private readonly ILogger<ProxyConfigurationReloadLogger> _logger;
    public ProxyConfigurationReloadLogger(ILogger<ProxyConfigurationReloadLogger> logger)
    {
        _logger = logger;
    }

    public void LoadFailed(string sourceDirectory, IReadOnlyList<string> errors)
    {
        if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Warning))
        {
            LogProxyConfigurationReloadFailedFrom10010(_logger, sourceDirectory, string.Join("; ", errors), null);
        }
    }

    public void Loaded(int version, string sourceDirectory)
    {
        if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Information))
        {
            LogProxyConfigurationVersionLoadedFrom10011(_logger, version, sourceDirectory, null);
        }
    }
}

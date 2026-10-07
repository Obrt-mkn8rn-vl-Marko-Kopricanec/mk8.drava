using Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
using Mk8.Drava.Application.BLL.ControlPlane.Status;
using Mk8.Drava.Application.BLL.ControlPlane.RuntimePreflight;
using Mk8.Drava.Application.INF.Runtime;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Mk8.Drava.Application.INF.Configuration.Loading;
public sealed partial class ProxyConfigurationStartupService : IHostedService
{
    private readonly IProxyConfigurationReloadOperations<ProxyConfigurationProjection> _reloadService;
    private readonly ProxyRuntimePreflightService _preflight;
    private readonly ILogger<ProxyConfigurationStartupService> _logger;
    public ProxyConfigurationStartupService(IProxyConfigurationReloadOperations<ProxyConfigurationProjection> reloadService, ProxyRuntimePreflightService preflight, ILogger<ProxyConfigurationStartupService> logger)
    {
        _reloadService = reloadService;
        _preflight = preflight;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var preflight = _preflight.RunStartupChecks();
        if (string.Equals(preflight.State, ProxyStatusText.Failed, StringComparison.OrdinalIgnoreCase))
        {
            var message = "MDRAVA runtime preflight failed: " + string.Join(", ", preflight.Reasons);
            if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Critical))
            {
                Log10004(_logger, message, null);
            }
            throw new InvalidOperationException(message);
        }

        if (string.Equals(preflight.State, ProxyStatusText.Degraded, StringComparison.OrdinalIgnoreCase))
        {
            if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Warning))
            {
                LogMDRAVARuntimePreflightCompletedWith10005(_logger, string.Join(", ", preflight.Reasons), null);
            }
        }

        var result = await _reloadService.ReloadAsync(cancellationToken).ConfigureAwait(false);
        if (result is not ProxyConfigurationReloadResult<ProxyConfigurationProjection>.ReloadedResult)
        {
            var message = $"MDRAVA could not load a valid proxy configuration from '{result.SourceDirectory}'. " + string.Join(" ", result.Errors);
            if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Critical))
            {
                Log10006(_logger, message, null);
            }
            throw new InvalidOperationException(message);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

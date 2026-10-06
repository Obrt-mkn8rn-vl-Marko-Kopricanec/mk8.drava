using Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
using Mk8.Drava.Application.BLL.ControlPlane.Status;
using Mk8.Drava.Application.BLL.ControlPlane.RuntimePreflight;
using Mk8.Drava.Application.INF.Runtime;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Mk8.Drava.Application.INF.Configuration.Loading;
public sealed class ProxyConfigurationStartupService : IHostedService
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
            _logger.LogCritical("{Message}", message);
            throw new InvalidOperationException(message);
        }

        if (string.Equals(preflight.State, ProxyStatusText.Degraded, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("MDRAVA runtime preflight completed with warnings: {Reasons}", string.Join(", ", preflight.Reasons));
        }

        var result = await _reloadService.ReloadAsync(cancellationToken).ConfigureAwait(false);
        if (result is not ProxyConfigurationReloadResult<ProxyConfigurationProjection>.ReloadedResult)
        {
            var message = $"MDRAVA could not load a valid proxy configuration from '{result.SourceDirectory}'. " + string.Join(" ", result.Errors);
            _logger.LogCritical("{Message}", message);
            throw new InvalidOperationException(message);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

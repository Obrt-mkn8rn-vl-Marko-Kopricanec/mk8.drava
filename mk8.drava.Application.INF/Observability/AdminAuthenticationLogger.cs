using Mk8.Drava.Application.BLL.ControlPlane.AdminAuthentication;
using Microsoft.Extensions.Logging;

namespace Mk8.Drava.Application.INF.Observability;
public sealed partial class AdminAuthenticationLogger : IProxyAdminAuthenticationEventSink
{
    private readonly ILogger<AdminAuthenticationLogger> _logger;
    public AdminAuthenticationLogger(ILogger<AdminAuthenticationLogger> logger)
    {
        _logger = logger;
    }

    public void ActiveConfigurationMissing()
    {
        if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Warning))
        {
            LogAdminRequestArrivedBeforeAn10009(_logger, null);
        }
    }
}

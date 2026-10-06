using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
using Mk8.Drava.Application.BLL.ControlPlane.Http3;

namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public static class ProxyLimitConfigurationSummarySourceMapper
{
    public static ProxyLimitConfigurationSummarySource FromSource(RuntimeLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        return new ProxyLimitConfigurationSummarySource(limits.MaxActiveClientConnections, limits.MaxConcurrentTlsHandshakes, limits.RequestsPerMinutePerIp);
    }
}

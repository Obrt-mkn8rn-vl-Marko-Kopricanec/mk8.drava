using System.Collections.ObjectModel;
using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.BLL.ControlPlane.Caching;
using Mk8.Drava.Application.BLL.ControlPlane.ConfigLint;
using Mk8.Drava.Application.BLL.ControlPlane.Http3;
using Mk8.Drava.Application.BLL.ControlPlane.Listeners;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;

namespace Mk8.Drava.Application.BLL.ControlPlane.Status;
public static class ProxyStatusConfigurationSummaryMapper
{
    public static ProxyStatusConfigurationSummary FromCounts(int version, DateTimeOffset loadedAtUtc, int listenerCount, int routeCount)
    {
        return new ProxyStatusConfigurationSummary(version, loadedAtUtc, listenerCount, routeCount);
    }
}

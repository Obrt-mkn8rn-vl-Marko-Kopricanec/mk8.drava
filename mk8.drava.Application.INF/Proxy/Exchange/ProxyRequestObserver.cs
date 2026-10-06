using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.RequestDiagnostics;
using Mk8.Drava.Application.BLL.Proxy;
using Mk8.Drava.Application.INF.Observability;

namespace Mk8.Drava.Application.INF.Proxy.Exchange;

public sealed class ProxyRequestObserver(AccessLogEmitter logs) : IProxyRequestObserver
{
    public void Complete(ProxyRequestContext context, ProxyConfigurationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(snapshot);
        logs.Complete(context, context.AccessLogEnabled ?? snapshot.Observability.AccessLogEnabled, snapshot.Observability.RecentDiagnosticsCapacity);
    }
}

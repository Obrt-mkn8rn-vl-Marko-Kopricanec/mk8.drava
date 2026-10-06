using Mk8.Drava.Application.BLL.ControlPlane.Caching;
using Mk8.Drava.Application.BLL.ControlPlane.Forwarding;
using Mk8.Drava.Application.BLL.ControlPlane.Routing;
using Mk8.Drava.Application.BLL.ControlPlane.Upgrades;

namespace Mk8.Drava.Application.BLL.Proxy;

public interface IProxyExchangeExecutor
{
    ValueTask GeneratedAsync(GeneratedRouteResponse response, string requestId, CancellationToken cancellationToken);
    ValueTask CachedAsync(CachedProxyResponse response, string requestId, CancellationToken cancellationToken);
    ValueTask<ForwardingResult> ForwardAsync(ProxyForwardingContext context, CancellationToken cancellationToken);
    ValueTask<ForwardingResult> UpgradeAsync(ProxyForwardingContext context, UpgradeRequestInfo upgrade, CancellationToken cancellationToken);
}

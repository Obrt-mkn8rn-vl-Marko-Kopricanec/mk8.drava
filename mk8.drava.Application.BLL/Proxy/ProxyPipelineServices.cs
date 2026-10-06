using Mk8.Drava.Application.BLL.ControlPlane.Acme;
using Mk8.Drava.Application.BLL.ControlPlane.Caching;
using Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement;
using Mk8.Drava.Application.BLL.ControlPlane.Headers;
using Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
using Mk8.Drava.Application.BLL.ControlPlane.Metrics;
using Mk8.Drava.Application.BLL.ControlPlane.RequestDiagnostics;
using Mk8.Drava.Application.BLL.ControlPlane.Resilience;
using Mk8.Drava.Application.BLL.ControlPlane.Routing;
using Mk8.Drava.Application.BLL.ControlPlane.RuntimeGuards;
using Mk8.Drava.Application.BLL.ControlPlane.Upgrades;
using Mk8.Drava.Application.BLL.ControlPlane.UpstreamSelection;

namespace Mk8.Drava.Application.BLL.Proxy;

public sealed class ProxyPipelineServices
{
    public required IProxyActiveConfigurationSnapshotReader Configuration { get; init; }
    public required IRouteMatcher RouteMatcher { get; init; }
    public required IUpstreamSelector Selector { get; init; }
    public required UpstreamHealthStore Health { get; init; }
    public required CircuitBreakerStore Circuits { get; init; }
    public required ForwardedHeadersPolicy ForwardedHeaders { get; init; }
    public required ProxyRouteActionPolicy RouteActions { get; init; }
    public required PathRewritePolicy PathRewrite { get; init; }
    public required UpgradeRequestPolicy Upgrades { get; init; }
    public required ResponseCacheStore Cache { get; init; }
    public required AcmeHttp01ChallengeResponder Challenges { get; init; }
    public required ClientRateLimiter RateLimiter { get; init; }
    public required ProxyMetrics Metrics { get; init; }
    public required RequestIdGenerator RequestIds { get; init; }
    public required IProxyRequestObserver Observer { get; init; }
    public required TimeProvider Clock { get; init; }
}

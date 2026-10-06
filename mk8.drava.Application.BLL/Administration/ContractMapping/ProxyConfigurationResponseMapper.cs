using BusinessProxyConfigurationProjection = Mk8.Drava.Application.BLL.ControlPlane.ConfigurationManagement.ProxyConfigurationProjection;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ProxyConfigurationResponseMapper
{
    public static ProxyConfigurationResponse FromProjection(BusinessProxyConfigurationProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new ProxyConfigurationResponse(version: projection.Version, loadedAtUtc: projection.LoadedAtUtc, sourceDirectory: projection.SourceDirectory, sourceFiles: projection.SourceFiles, discovery: ProxyConfigurationDiscoveryResponseMapper.FromDiscovery(projection.Discovery), adminSecurity: RuntimeAdminSecurityResponseMapper.FromProjection(projection.AdminSecurity), acme: RuntimeAcmeResponseMapper.FromProjection(projection.Acme), timeouts: RuntimeTimeoutsResponseMapper.FromProjection(projection.Timeouts), connectionLimits: RuntimeConnectionLimitsResponseMapper.FromProjection(projection.ConnectionLimits), observability: RuntimeObservabilityResponseMapper.FromProjection(projection.Observability), limits: RuntimeLimitsResponseMapper.FromProjection(projection.Limits), forwardedHeaders: RuntimeForwardedHeadersResponseMapper.FromProjection(projection.ForwardedHeaders), metrics: RuntimeMetricsResponseMapper.FromProjection(projection.Metrics), http3: RuntimeHttp3SupportResponseMapper.FromProjection(projection.Http3), certificates: RuntimeCertificateResponseMapper.FromCertificates(projection.Certificates), listeners: RuntimeListenerResponseMapper.FromListeners(projection.Listeners), routes: RuntimeRouteResponseMapper.FromRoutes(projection.Routes));
    }
}

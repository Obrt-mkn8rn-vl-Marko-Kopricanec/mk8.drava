using BusinessRuntimeHttp3AltSvcProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeHttp3AltSvcProjection;
using BusinessRuntimeHttp3Enablement = Mk8.Drava.Application.BLL.Configuration.RuntimeHttp3Enablement;
using BusinessRuntimeHttp3ListenerReadinessProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeHttp3ListenerReadinessProjection;
using BusinessRuntimeQuicListenerIdentityProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeQuicListenerIdentityProjection;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class RuntimeHttp3ListenerReadinessResponseMapper
{
    public static RuntimeHttp3ListenerReadinessResponse FromProjection(BusinessRuntimeHttp3ListenerReadinessProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RuntimeHttp3ListenerReadinessResponse(projection.Configured, projection.DefaultEnabled, projection.EnablementLevel, projection.EnabledForTraffic, projection.DisabledReason, projection.AltSvcConfigured, projection.AltSvcMaxAgeSeconds, projection.UdpQuicListenerIdentityModeled, projection.QuicIdentity is null ? null : RuntimeQuicListenerIdentityResponseMapper.FromProjection(projection.QuicIdentity));
    }
}

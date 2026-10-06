using BusinessRuntimeHttp3SupportProjection = Mk8.Drava.Application.BLL.ControlPlane.Http3.RuntimeHttp3SupportProjection;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class RuntimeHttp3SupportResponseMapper
{
    public static RuntimeHttp3SupportResponse FromProjection(BusinessRuntimeHttp3SupportProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RuntimeHttp3SupportResponse(projection.RuntimeSupport, projection.QuicListenerSupported, projection.QuicConnectionSupported, projection.Configured, projection.EnablementLevel, projection.EnabledForTraffic, projection.QuicListenerReady, projection.AltSvcConfigured, projection.AltSvcActive, projection.AltSvcMaxAgeSeconds, projection.DisabledReason, projection.UdpQuicListenerIdentityModeled, projection.ReadinessConclusion, projection.DefaultEnablementState, projection.DefaultReadinessBlockers, projection.AltSvcStateReason, projection.QpackMode, projection.QpackDynamicTableCapacity, projection.QpackBlockedStreams, projection.RequestBodyMode, projection.ClientHttp3SupportLevel, projection.UpstreamHttp3SupportLevel, projection.ClientProtocols, projection.UpstreamProtocols, projection.SupportedRouteActions, projection.SupportedPolicyFeatures, projection.UnsupportedFeatures, projection.UpstreamHttp3Configured, projection.UpstreamPoolingMode, projection.UpstreamMultiplexingEnabled, projection.UpstreamMaxStreamsPerConnection, projection.UpstreamQpackMode, projection.UpstreamPoolingLimitationReason);
    }
}

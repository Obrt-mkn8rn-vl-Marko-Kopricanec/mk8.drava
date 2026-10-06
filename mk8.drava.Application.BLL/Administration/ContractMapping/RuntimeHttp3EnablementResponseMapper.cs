using BusinessRuntimeHttp3AltSvcProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeHttp3AltSvcProjection;
using BusinessRuntimeHttp3Enablement = Mk8.Drava.Application.BLL.Configuration.RuntimeHttp3Enablement;
using BusinessRuntimeHttp3ListenerReadinessProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeHttp3ListenerReadinessProjection;
using BusinessRuntimeQuicListenerIdentityProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeQuicListenerIdentityProjection;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class RuntimeHttp3EnablementResponseMapper
{
    public static RuntimeHttp3EnablementResponse FromEnablement(BusinessRuntimeHttp3Enablement enablement)
    {
        return enablement switch
        {
            BusinessRuntimeHttp3Enablement.Default => RuntimeHttp3EnablementResponse.Default,
            BusinessRuntimeHttp3Enablement.Disabled => RuntimeHttp3EnablementResponse.Disabled,
            _ => throw new ArgumentOutOfRangeException(nameof(enablement), enablement, null)};
    }
}

using BusinessRuntimeHttp3AltSvcProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeHttp3AltSvcProjection;
using BusinessRuntimeHttp3Enablement = Mk8.Drava.Application.BLL.Configuration.RuntimeHttp3Enablement;
using BusinessRuntimeHttp3ListenerReadinessProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeHttp3ListenerReadinessProjection;
using BusinessRuntimeQuicListenerIdentityProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeQuicListenerIdentityProjection;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class RuntimeQuicListenerIdentityResponseMapper
{
    public static RuntimeQuicListenerIdentityResponse FromProjection(BusinessRuntimeQuicListenerIdentityProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RuntimeQuicListenerIdentityResponse(name: projection.Name, address: projection.Address, port: projection.Port, tlsEnabled: projection.TlsEnabled, key: projection.Key, bindKey: projection.BindKey);
    }
}

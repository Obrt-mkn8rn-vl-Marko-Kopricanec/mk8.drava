using BusinessRuntimeListenerIdentityProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeListenerIdentityProjection;
using BusinessRuntimeListenerTransport = Mk8.Drava.Application.BLL.Configuration.RuntimeListenerTransport;
using BusinessRuntimeSniCertificateBindingProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeSniCertificateBindingProjection;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class RuntimeListenerIdentityResponseMapper
{
    public static RuntimeListenerIdentityResponse FromProjection(BusinessRuntimeListenerIdentityProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RuntimeListenerIdentityResponse(name: projection.Name, address: projection.Address, port: projection.Port, transport: RuntimeListenerTransportResponseMapper.FromTransport(projection.Transport), tlsEnabled: projection.TlsEnabled, key: projection.Key, bindKey: projection.BindKey);
    }
}

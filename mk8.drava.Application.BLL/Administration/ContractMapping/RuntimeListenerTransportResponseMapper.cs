using BusinessRuntimeListenerIdentityProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeListenerIdentityProjection;
using BusinessRuntimeListenerTransport = Mk8.Drava.Application.BLL.Configuration.RuntimeListenerTransport;
using BusinessRuntimeSniCertificateBindingProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeSniCertificateBindingProjection;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class RuntimeListenerTransportResponseMapper
{
    public static RuntimeListenerTransportResponse FromTransport(BusinessRuntimeListenerTransport transport)
    {
        return transport switch
        {
            BusinessRuntimeListenerTransport.Http => RuntimeListenerTransportResponse.Http,
            BusinessRuntimeListenerTransport.Https => RuntimeListenerTransportResponse.Https,
            _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, null)};
    }
}

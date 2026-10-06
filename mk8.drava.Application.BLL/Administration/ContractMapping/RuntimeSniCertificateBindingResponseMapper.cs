using BusinessRuntimeListenerIdentityProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeListenerIdentityProjection;
using BusinessRuntimeListenerTransport = Mk8.Drava.Application.BLL.Configuration.RuntimeListenerTransport;
using BusinessRuntimeSniCertificateBindingProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeSniCertificateBindingProjection;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class RuntimeSniCertificateBindingResponseMapper
{
    public static IReadOnlyList<RuntimeSniCertificateBindingResponse> FromBindings(IReadOnlyList<BusinessRuntimeSniCertificateBindingProjection> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        return ApiResponseList.Copy(bindings.Select(FromBinding));
    }

    private static RuntimeSniCertificateBindingResponse FromBinding(BusinessRuntimeSniCertificateBindingProjection binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return new RuntimeSniCertificateBindingResponse(binding.HostName, binding.CertificateId);
    }
}

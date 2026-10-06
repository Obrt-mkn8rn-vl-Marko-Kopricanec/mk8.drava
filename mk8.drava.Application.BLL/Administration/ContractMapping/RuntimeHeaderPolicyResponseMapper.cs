using BusinessRuntimeHeaderFieldProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeHeaderFieldProjection;
using BusinessRuntimeHeaderPolicyProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeHeaderPolicyProjection;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class RuntimeHeaderPolicyResponseMapper
{
    public static RuntimeHeaderPolicyResponse FromProjection(BusinessRuntimeHeaderPolicyProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RuntimeHeaderPolicyResponse(setRequestHeaders: RuntimeHeaderFieldResponseMapper.FromFields(projection.SetRequestHeaders), removeRequestHeaders: projection.RemoveRequestHeaders, setResponseHeaders: RuntimeHeaderFieldResponseMapper.FromFields(projection.SetResponseHeaders), removeResponseHeaders: projection.RemoveResponseHeaders);
    }
}

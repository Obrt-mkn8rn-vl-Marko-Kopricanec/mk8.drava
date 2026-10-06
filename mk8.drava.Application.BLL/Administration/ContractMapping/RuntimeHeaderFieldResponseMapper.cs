using BusinessRuntimeHeaderFieldProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeHeaderFieldProjection;
using BusinessRuntimeHeaderPolicyProjection = Mk8.Drava.Application.BLL.Configuration.RuntimeHeaderPolicyProjection;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class RuntimeHeaderFieldResponseMapper
{
    public static IReadOnlyList<RuntimeHeaderFieldResponse> FromFields(IReadOnlyList<BusinessRuntimeHeaderFieldProjection> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return ApiResponseList.Copy(fields.Select(FromField));
    }

    private static RuntimeHeaderFieldResponse FromField(BusinessRuntimeHeaderFieldProjection field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return new RuntimeHeaderFieldResponse(field.Name, field.Value);
    }
}

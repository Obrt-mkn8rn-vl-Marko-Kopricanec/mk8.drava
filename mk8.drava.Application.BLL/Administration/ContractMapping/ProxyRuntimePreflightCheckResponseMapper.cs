using BusinessProxyRuntimePreflightCheck = Mk8.Drava.Application.BLL.ControlPlane.Status.ProxyRuntimePreflightCheck;
using BusinessProxyRuntimePreflightStatus = Mk8.Drava.Application.BLL.ControlPlane.Status.ProxyRuntimePreflightStatus;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ProxyRuntimePreflightCheckResponseMapper
{
    public static IReadOnlyList<ProxyRuntimePreflightCheckResponse> FromChecks(IEnumerable<BusinessProxyRuntimePreflightCheck> checks)
    {
        return ApiResponseList.Copy(checks.Select(FromCheck));
    }

    public static ProxyRuntimePreflightCheckResponse FromCheck(BusinessProxyRuntimePreflightCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        return new ProxyRuntimePreflightCheckResponse(check.Name, check.RelativePath, check.Exists, check.Created, check.CanRead, check.CanWrite, check.Severity, check.Reason);
    }
}

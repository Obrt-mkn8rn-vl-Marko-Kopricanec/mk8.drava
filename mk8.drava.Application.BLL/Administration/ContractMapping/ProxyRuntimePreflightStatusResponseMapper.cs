using BusinessProxyRuntimePreflightCheck = Mk8.Drava.Application.BLL.ControlPlane.Status.ProxyRuntimePreflightCheck;
using BusinessProxyRuntimePreflightStatus = Mk8.Drava.Application.BLL.ControlPlane.Status.ProxyRuntimePreflightStatus;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ProxyRuntimePreflightStatusResponseMapper
{
    public static ProxyRuntimePreflightStatusResponse FromStatus(BusinessProxyRuntimePreflightStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return new ProxyRuntimePreflightStatusResponse(status.State, status.GeneratedAtUtc, status.Reasons, ProxyRuntimePreflightCheckResponseMapper.FromChecks(status.Checks));
    }
}

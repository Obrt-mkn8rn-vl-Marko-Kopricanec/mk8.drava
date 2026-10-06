using BusinessProxyReadinessStatus = Mk8.Drava.Application.BLL.ControlPlane.Status.ProxyReadinessStatus;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ProxyReadinessStatusResponseMapper
{
    public static ProxyReadinessStatusResponse FromStatus(BusinessProxyReadinessStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return new ProxyReadinessStatusResponse(state: status.State, reasons: status.Reasons, generatedAtUtc: status.GeneratedAtUtc, configGeneration: status.ConfigGeneration);
    }
}

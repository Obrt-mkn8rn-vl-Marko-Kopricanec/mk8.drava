using BusinessProxyLogPersistenceFailureStatus = Mk8.Drava.Application.BLL.ControlPlane.Status.ProxyLogPersistenceFailureStatus;
using BusinessProxyLogPersistenceStatus = Mk8.Drava.Application.BLL.ControlPlane.Status.ProxyLogPersistenceStatus;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ProxyLogPersistenceFailureStatusResponseMapper
{
    public static ProxyLogPersistenceFailureStatusResponse FromStatus(BusinessProxyLogPersistenceFailureStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return new ProxyLogPersistenceFailureStatusResponse(status.TimestampUtc, status.Category, status.Reason);
    }
}

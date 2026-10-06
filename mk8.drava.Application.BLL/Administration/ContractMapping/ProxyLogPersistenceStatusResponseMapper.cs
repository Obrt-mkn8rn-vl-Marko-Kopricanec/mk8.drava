using BusinessProxyLogPersistenceFailureStatus = Mk8.Drava.Application.BLL.ControlPlane.Status.ProxyLogPersistenceFailureStatus;
using BusinessProxyLogPersistenceStatus = Mk8.Drava.Application.BLL.ControlPlane.Status.ProxyLogPersistenceStatus;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ProxyLogPersistenceStatusResponseMapper
{
    public static ProxyLogPersistenceStatusResponse FromStatus(BusinessProxyLogPersistenceStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return new ProxyLogPersistenceStatusResponse(status.AccessLogEnabled, status.AdminAuditEnabled, status.LogDirectory, status.MaxFileBytes, status.MaxFiles, status.State, status.Reason, status.LastSuccessfulWriteAtUtc, status.LastWriteFailure is null ? null : ProxyLogPersistenceFailureStatusResponseMapper.FromStatus(status.LastWriteFailure));
    }
}

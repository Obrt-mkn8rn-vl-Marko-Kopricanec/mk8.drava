using BusinessProxyBackupDirectoryStatus = Mk8.Drava.Application.BLL.ControlPlane.Backup.ProxyBackupDirectoryStatus;
using BusinessProxyBackupManifestCount = Mk8.Drava.Application.BLL.ControlPlane.Backup.ProxyBackupManifestCount;
using BusinessProxyBackupManifestEntry = Mk8.Drava.Application.BLL.ControlPlane.Backup.ProxyBackupManifestEntry;
using BusinessProxyBackupWarning = Mk8.Drava.Application.BLL.ControlPlane.Backup.ProxyBackupWarning;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ProxyBackupDirectoryStatusResponseMapper
{
    public static IReadOnlyList<ProxyBackupDirectoryStatusResponse> FromStatuses(IReadOnlyList<BusinessProxyBackupDirectoryStatus> statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        return ApiResponseList.Copy(statuses.Select(FromStatus));
    }

    private static ProxyBackupDirectoryStatusResponse FromStatus(BusinessProxyBackupDirectoryStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return new ProxyBackupDirectoryStatusResponse(status.RelativePath, status.Exists, status.Classification, status.Sensitive);
    }
}

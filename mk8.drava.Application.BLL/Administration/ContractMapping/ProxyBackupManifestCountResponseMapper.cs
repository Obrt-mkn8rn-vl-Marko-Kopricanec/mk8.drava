using BusinessProxyBackupDirectoryStatus = Mk8.Drava.Application.BLL.ControlPlane.Backup.ProxyBackupDirectoryStatus;
using BusinessProxyBackupManifestCount = Mk8.Drava.Application.BLL.ControlPlane.Backup.ProxyBackupManifestCount;
using BusinessProxyBackupManifestEntry = Mk8.Drava.Application.BLL.ControlPlane.Backup.ProxyBackupManifestEntry;
using BusinessProxyBackupWarning = Mk8.Drava.Application.BLL.ControlPlane.Backup.ProxyBackupWarning;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ProxyBackupManifestCountResponseMapper
{
    public static IReadOnlyList<ProxyBackupManifestCountResponse> FromCounts(IReadOnlyList<BusinessProxyBackupManifestCount> counts)
    {
        ArgumentNullException.ThrowIfNull(counts);
        return ApiResponseList.Copy(counts.Select(FromCount));
    }

    private static ProxyBackupManifestCountResponse FromCount(BusinessProxyBackupManifestCount count)
    {
        ArgumentNullException.ThrowIfNull(count);
        return new ProxyBackupManifestCountResponse(count.Category, count.Classification, count.Count, count.SizeBytes);
    }
}

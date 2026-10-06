using BusinessProxyBackupDirectoryStatus = Mk8.Drava.Application.BLL.ControlPlane.Backup.ProxyBackupDirectoryStatus;
using BusinessProxyBackupManifestCount = Mk8.Drava.Application.BLL.ControlPlane.Backup.ProxyBackupManifestCount;
using BusinessProxyBackupManifestEntry = Mk8.Drava.Application.BLL.ControlPlane.Backup.ProxyBackupManifestEntry;
using BusinessProxyBackupWarning = Mk8.Drava.Application.BLL.ControlPlane.Backup.ProxyBackupWarning;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ProxyBackupManifestEntryResponseMapper
{
    public static IReadOnlyList<ProxyBackupManifestEntryResponse> FromEntries(IReadOnlyList<BusinessProxyBackupManifestEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return ApiResponseList.Copy(entries.Select(FromEntry));
    }

    private static ProxyBackupManifestEntryResponse FromEntry(BusinessProxyBackupManifestEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new ProxyBackupManifestEntryResponse(entry.RelativePath, entry.Category, entry.Classification, entry.Sensitive, entry.SizeBytes, entry.LastWriteTimeUtc);
    }
}

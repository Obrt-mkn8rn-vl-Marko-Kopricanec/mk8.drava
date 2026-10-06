using BusinessProxyBackupManifest = Mk8.Drava.Application.BLL.ControlPlane.Backup.ProxyBackupManifest;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ProxyBackupManifestResponseMapper
{
    public static ProxyBackupManifestResponse FromManifest(BusinessProxyBackupManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return new ProxyBackupManifestResponse(generatedAtUtc: manifest.GeneratedAtUtc, directories: ProxyBackupDirectoryStatusResponseMapper.FromStatuses(manifest.Directories), entries: ProxyBackupManifestEntryResponseMapper.FromEntries(manifest.Entries), counts: ProxyBackupManifestCountResponseMapper.FromCounts(manifest.Counts), warnings: ProxyBackupWarningResponseMapper.FromWarnings(manifest.Warnings), truncated: manifest.Truncated);
    }
}

using BusinessProxyBackupDirectoryStatus = Mk8.Drava.Application.BLL.ControlPlane.Backup.ProxyBackupDirectoryStatus;
using BusinessProxyBackupManifestCount = Mk8.Drava.Application.BLL.ControlPlane.Backup.ProxyBackupManifestCount;
using BusinessProxyBackupManifestEntry = Mk8.Drava.Application.BLL.ControlPlane.Backup.ProxyBackupManifestEntry;
using BusinessProxyBackupWarning = Mk8.Drava.Application.BLL.ControlPlane.Backup.ProxyBackupWarning;
using Mk8.Drava.Contracts.Administration.V1;

namespace Mk8.Drava.Application.BLL.Administration.ContractMapping;
public static class ProxyBackupWarningResponseMapper
{
    public static IReadOnlyList<ProxyBackupWarningResponse> FromWarnings(IReadOnlyList<BusinessProxyBackupWarning> warnings)
    {
        ArgumentNullException.ThrowIfNull(warnings);
        return ApiResponseList.Copy(warnings.Select(FromWarning));
    }

    private static ProxyBackupWarningResponse FromWarning(BusinessProxyBackupWarning warning)
    {
        ArgumentNullException.ThrowIfNull(warning);
        return new ProxyBackupWarningResponse(warning.Code, warning.Message, warning.RelativePath);
    }
}

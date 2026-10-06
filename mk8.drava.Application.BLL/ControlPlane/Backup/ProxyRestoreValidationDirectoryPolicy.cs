namespace Mk8.Drava.Application.BLL.ControlPlane.Backup;
public static class ProxyRestoreValidationDirectoryPolicy
{
    public static IReadOnlyList<ProxyRestoreValidationFinding> FindMissingRequiredDirectories(IReadOnlyList<ProxyBackupDirectoryStatus> directories)
    {
        return directories.Where(static directory => !directory.Exists && string.Equals(directory.Classification, ProxyBackupFileClassificationPolicy.MustBackup, StringComparison.OrdinalIgnoreCase)).Select(static directory => ProxyRestoreValidationFindingPolicy.RequiredDirectoryMissing(directory.RelativePath)).ToArray();
    }
}

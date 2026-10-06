namespace Mk8.Drava.Application.BLL.ControlPlane.Backup;
public static class ProxyBackupDirectoryLayoutPolicy
{
    public static IReadOnlyList<ProxyBackupDirectoryRequirement> ExpectedDirectories()
    {
        return[new ProxyBackupDirectoryRequirement("config", ProxyBackupFileClassificationPolicy.MustBackup, false), new ProxyBackupDirectoryRequirement("config/sites", ProxyBackupFileClassificationPolicy.MustBackup, false), new ProxyBackupDirectoryRequirement("logs", ProxyBackupFileClassificationPolicy.ShouldBackup, false), new ProxyBackupDirectoryRequirement("certs", ProxyBackupFileClassificationPolicy.NeverExportByDefaultSensitive, true), new ProxyBackupDirectoryRequirement("certs/acme", ProxyBackupFileClassificationPolicy.NeverExportByDefaultSensitive, true), new ProxyBackupDirectoryRequirement("state", ProxyBackupFileClassificationPolicy.ShouldBackup, false)];
    }
}

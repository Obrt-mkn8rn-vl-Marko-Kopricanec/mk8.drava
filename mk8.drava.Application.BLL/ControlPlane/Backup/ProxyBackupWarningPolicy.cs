namespace Mk8.Drava.Application.BLL.ControlPlane.Backup;
public static class ProxyBackupWarningPolicy
{
    public static ProxyBackupWarning DataDirectoryMissing()
    {
        return new ProxyBackupWarning("data_directory_missing", "The MDRAVA data directory does not exist.", null);
    }

    public static ProxyBackupWarning MissingDirectory(string relativePath)
    {
        return new ProxyBackupWarning("missing_directory", "An expected data-directory child is missing.", relativePath);
    }

    public static ProxyBackupWarning ManifestTruncated()
    {
        return new ProxyBackupWarning("manifest_truncated", "The backup manifest reached its bounded entry limit.", null);
    }

    public static ProxyBackupWarning FromFileSystemWarning(ProxyBackupFileSystemWarning warning)
    {
        ArgumentNullException.ThrowIfNull(warning);
        return warning.Code switch
        {
            "directory_unreadable" => new ProxyBackupWarning(warning.Code, "A directory could not be inspected.", warning.RelativePath),
            "reparse_point_skipped" => new ProxyBackupWarning(warning.Code, "A reparse point was skipped during backup manifest generation.", warning.RelativePath),
            "unsafe_path_skipped" => new ProxyBackupWarning(warning.Code, "A file path could not be represented as a safe data-directory relative path.", warning.RelativePath),
            _ => new ProxyBackupWarning(warning.Code, "A backup filesystem path could not be inspected.", warning.RelativePath)};
    }
}

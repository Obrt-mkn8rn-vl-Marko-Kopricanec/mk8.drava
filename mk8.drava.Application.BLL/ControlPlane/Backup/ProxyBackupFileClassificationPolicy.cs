namespace Mk8.Drava.Application.BLL.ControlPlane.Backup;
public static class ProxyBackupFileClassificationPolicy
{
    public const string MustBackup = "must_backup";
    public const string ShouldBackup = "should_backup";
    public const string OptionalBackup = "optional_backup";
    public const string RuntimeGeneratedSafeToOmit = "runtime_generated_safe_to_omit";
    public const string NeverExportByDefaultSensitive = "never_export_by_default_sensitive";
    public static ProxyBackupFileClassification ClassifyFile(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        var normalized = relativePath.Replace('\\', '/');
        var slash = normalized.LastIndexOf('/');
        var fileName = slash >= 0 ? normalized[(slash + 1)..] : normalized;
        if (string.Equals(fileName, "example.site.yaml", StringComparison.OrdinalIgnoreCase) || fileName.StartsWith("example.", StringComparison.OrdinalIgnoreCase))
        {
            return new ProxyBackupFileClassification("generated_example", RuntimeGeneratedSafeToOmit, false);
        }

        if (string.Equals(normalized, "config/proxy.json", StringComparison.OrdinalIgnoreCase))
        {
            return new ProxyBackupFileClassification("config", MustBackup, false);
        }

        if (normalized.StartsWith("config/sites/", StringComparison.OrdinalIgnoreCase))
        {
            return new ProxyBackupFileClassification("site_config", MustBackup, false);
        }

        if (normalized.StartsWith("logs/", StringComparison.OrdinalIgnoreCase))
        {
            return new ProxyBackupFileClassification("logs", ShouldBackup, false);
        }

        if (normalized.StartsWith("certs/acme/accounts/", StringComparison.OrdinalIgnoreCase) || normalized.StartsWith("certs/acme/private-keys/", StringComparison.OrdinalIgnoreCase))
        {
            return new ProxyBackupFileClassification("acme_secret_material", NeverExportByDefaultSensitive, true);
        }

        if (normalized.StartsWith("certs/acme/certificates/", StringComparison.OrdinalIgnoreCase) || normalized.StartsWith("certs/acme/metadata/", StringComparison.OrdinalIgnoreCase))
        {
            return new ProxyBackupFileClassification("acme_certificate_state", ShouldBackup, false);
        }

        if (normalized.StartsWith("certs/", StringComparison.OrdinalIgnoreCase) && (fileName.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".p12", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".key", StringComparison.OrdinalIgnoreCase)))
        {
            return new ProxyBackupFileClassification("manual_certificate_material", NeverExportByDefaultSensitive, true);
        }

        if (normalized.StartsWith("certs/", StringComparison.OrdinalIgnoreCase))
        {
            return new ProxyBackupFileClassification("certificate_metadata", ShouldBackup, false);
        }

        if (normalized.StartsWith("state/", StringComparison.OrdinalIgnoreCase))
        {
            return new ProxyBackupFileClassification("state", ShouldBackup, false);
        }

        return new ProxyBackupFileClassification("unknown", OptionalBackup, false);
    }
}

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
        if (ClassifyPrivateState(normalized, fileName) is { } privateState) return privateState;
        if (string.Equals(fileName, "example.site.yaml", StringComparison.OrdinalIgnoreCase) || fileName.StartsWith("example.", StringComparison.OrdinalIgnoreCase))
        {
            return new ProxyBackupFileClassification("generated_example", RuntimeGeneratedSafeToOmit, Sensitive: false);
        }

        if (string.Equals(normalized, "config/proxy.json", StringComparison.OrdinalIgnoreCase))
        {
            return new ProxyBackupFileClassification("config", MustBackup, Sensitive: false);
        }

        if (normalized.StartsWith("config/sites/", StringComparison.OrdinalIgnoreCase))
        {
            return new ProxyBackupFileClassification("site_config", MustBackup, Sensitive: false);
        }

        if (normalized.StartsWith("logs/", StringComparison.OrdinalIgnoreCase))
        {
            return new ProxyBackupFileClassification("logs", ShouldBackup, Sensitive: false);
        }

        if (normalized.StartsWith("certs/acme/certificates/", StringComparison.OrdinalIgnoreCase) || normalized.StartsWith("certs/acme/metadata/", StringComparison.OrdinalIgnoreCase))
        {
            return new ProxyBackupFileClassification("acme_certificate_state", ShouldBackup, Sensitive: false);
        }

        if (normalized.StartsWith("certs/", StringComparison.OrdinalIgnoreCase))
        {
            return new ProxyBackupFileClassification("certificate_metadata", ShouldBackup, Sensitive: false);
        }

        if (normalized.StartsWith("state/", StringComparison.OrdinalIgnoreCase))
        {
            return new ProxyBackupFileClassification("state", ShouldBackup, Sensitive: false);
        }

        return new ProxyBackupFileClassification("unknown", OptionalBackup, Sensitive: false);
    }

    private static ProxyBackupFileClassification? ClassifyPrivateState(string path, string fileName)
    {
        if (string.Equals(path, "registry.sqlite", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(path, "registry.sqlite-wal", StringComparison.OrdinalIgnoreCase))
        {
            return new ProxyBackupFileClassification("controller_registry", MustBackup, Sensitive: true);
        }
        if (string.Equals(path, "registry.sqlite-shm", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(path, "application.lock", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(path, "registry-writer.lock", StringComparison.OrdinalIgnoreCase))
        {
            return new ProxyBackupFileClassification("process_coordination", RuntimeGeneratedSafeToOmit, Sensitive: false);
        }

        var acmeSecretDirectory = path.StartsWith("certs/acme/accounts/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("certs/acme/private-keys/", StringComparison.OrdinalIgnoreCase);
        var privateExtension = fileName.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".p12", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".key", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".token", StringComparison.OrdinalIgnoreCase);
        if (!acmeSecretDirectory && !privateExtension && !string.Equals(path, "gateway-serving.plan", StringComparison.OrdinalIgnoreCase)) return null;
        var category = path.StartsWith("certs/acme/", StringComparison.OrdinalIgnoreCase) ? "acme_secret_material" :
            path.StartsWith("certs/", StringComparison.OrdinalIgnoreCase) ? "manual_certificate_material" : "private_credential_material";
        return new ProxyBackupFileClassification(category, NeverExportByDefaultSensitive, Sensitive: true);
    }
}

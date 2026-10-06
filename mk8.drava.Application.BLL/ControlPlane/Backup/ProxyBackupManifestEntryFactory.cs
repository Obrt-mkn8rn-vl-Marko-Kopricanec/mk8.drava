namespace Mk8.Drava.Application.BLL.ControlPlane.Backup;
public static class ProxyBackupManifestEntryFactory
{
    public static ProxyBackupManifestEntry FromFileSystemEntry(ProxyBackupFileSystemEntry file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var classification = ProxyBackupFileClassificationPolicy.ClassifyFile(file.RelativePath);
        return new ProxyBackupManifestEntry(file.RelativePath, classification.Category, classification.Classification, classification.Sensitive, file.SizeBytes, file.LastWriteTimeUtc);
    }
}

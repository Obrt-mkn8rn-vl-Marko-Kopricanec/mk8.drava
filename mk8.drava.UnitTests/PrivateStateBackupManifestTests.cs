using System.Text.Json;
using Mk8.Drava.Application.BLL.ControlPlane.Backup;
using Mk8.Drava.Application.DAL.Configuration.Paths;
using Mk8.Drava.Application.DAL.DataDirectory;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class PrivateStateBackupManifestTests
{
    [Fact]
    public void PrivateStateInventoryRequiresRegistryAndProtectsKeysWithoutReadingPayloads()
    {
        using var directory = new RegistryStateDirectory();
        const string marker = "synthetic-private-backup-payload";
        string[] paths = ["registry.sqlite", "registry.sqlite-wal", "registry.sqlite-shm", "application.lock", "registry-writer.lock",
            "gateway-serving.plan", "administrator.token", "example.issuer.pfx", "certs/acme/accounts/example.json", "certs/acme/certificates/site/current.pfx"];
        foreach (var relative in paths)
        {
            var path = Path.Combine(directory.Path, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Fixture path has no parent."));
            File.WriteAllText(path, marker);
        }
        var scan = new ProxyBackupFileSystem(new ProxyDataDirectoryPathSafety()).ScanDataDirectory(directory.Path);
        Assert.Empty(scan.Warnings);
        var entries = new Dictionary<string, ProxyBackupManifestEntry>(StringComparer.Ordinal);
        foreach (var file in scan.Files) entries.Add(file.RelativePath, ProxyBackupManifestEntryFactory.FromFileSystemEntry(file));
        Assert.Equal(paths.Length, entries.Count);
        foreach (var path in new[] { "registry.sqlite", "registry.sqlite-wal" })
        {
            Assert.Equal("controller_registry", entries[path].Category);
            Assert.Equal(ProxyBackupFileClassificationPolicy.MustBackup, entries[path].Classification);
            Assert.True(entries[path].Sensitive);
        }
        foreach (var path in new[] { "registry.sqlite-shm", "application.lock", "registry-writer.lock" })
        {
            Assert.Equal(ProxyBackupFileClassificationPolicy.RuntimeGeneratedSafeToOmit, entries[path].Classification);
            Assert.False(entries[path].Sensitive);
        }
        foreach (var path in new[] { "gateway-serving.plan", "administrator.token", "example.issuer.pfx", "certs/acme/accounts/example.json", "certs/acme/certificates/site/current.pfx" })
        {
            Assert.Equal(ProxyBackupFileClassificationPolicy.NeverExportByDefaultSensitive, entries[path].Classification);
            Assert.True(entries[path].Sensitive);
        }
        Assert.DoesNotContain(marker, JsonSerializer.Serialize(entries), StringComparison.Ordinal);
        foreach (var path in paths) Assert.Equal(marker, File.ReadAllText(Path.Combine(directory.Path, path)));
    }

    [Fact]
    public void OrdinaryExamplesAndPublicCertificateMetadataKeepTheirExistingPurpose()
    {
        var example = ProxyBackupFileClassificationPolicy.ClassifyFile("config/sites/example.site.yaml");
        Assert.Equal(ProxyBackupFileClassificationPolicy.RuntimeGeneratedSafeToOmit, example.Classification);
        Assert.False(example.Sensitive);
        var certificate = ProxyBackupFileClassificationPolicy.ClassifyFile("certs/acme/certificates/site/chain.cer");
        Assert.Equal(ProxyBackupFileClassificationPolicy.ShouldBackup, certificate.Classification);
        Assert.False(certificate.Sensitive);
        var unrelated = ProxyBackupFileClassificationPolicy.ClassifyFile("logs/registry.sqlite");
        Assert.Equal("logs", unrelated.Category);
        Assert.Equal(ProxyBackupFileClassificationPolicy.ShouldBackup, unrelated.Classification);
    }
}

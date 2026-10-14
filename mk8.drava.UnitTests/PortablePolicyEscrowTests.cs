using System.Security.Cryptography;
using System.Text;
using Mk8.Drava.Application.BLL.NoConf;
using Mk8.Drava.Configuration;
using Supprocom.Secrets;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class PortablePolicyEscrowTests
{
    [Fact]
    public async Task ExportContainsOnlyPolicyMaterialAndUsesANewProtectionKeyAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var source = CreateEnvironment(directory.Path, "source");
        var destination = CreateEnvironment(directory.Path, "export");
        var reference = PolicyPayloadSnapshot.Describe("{\"policy\":\"synthetic-policy-value\"}");
        var archive = PortablePolicyPayloadStoreTests.ArchiveFixture(new PolicyPayloadSnapshot("synthetic-site",
            [new KeyValuePair<string, string>(reference.Digest, "{\"policy\":\"synthetic-policy-value\"}")]));
        var path = Path.Combine(source, ".env");
        await PortableFixtureFile.WriteAsync(path, SupprocomSecretDocument.Serialize([
            new SupprocomSecretSetting("Drava:ApplicationBootstrap", BootstrapFile.Serialize(new ApplicationBootstrap
            {
                SiteId = "synthetic-site", StateDirectory = directory.Path,
                AdministratorToken = new SecretText(new string('A', 32)),
            })),
            new SupprocomSecretSetting(InstallationCollectionBounds.PolicyArchiveKey, archive.Reveal()),
            new SupprocomSecretSetting("Unrelated:ServiceCredential", "synthetic-service-credential"),
        ]), CancellationToken.None).ConfigureAwait(true);
        _ = await PortableConfiguration.LoadDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        var captured = await PortablePolicyEscrow.ReadApplicationArchiveAsync(path, "synthetic-site", directory.Path, allowCredentialEscrow: true, CancellationToken.None).ConfigureAwait(true);
        await PortablePolicyEscrow.ExportAsync(destination, "synthetic-site", captured, allowCredentialEscrow: true, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(archive, await PortablePolicyEscrow.LoadAsync(destination, "synthetic-site", allowCredentialEscrow: true, CancellationToken.None).ConfigureAwait(true));
        var settings = await PortablePolicyPayloadStoreTests.Store(destination).LoadAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(2, settings.Count);
        Assert.DoesNotContain("Drava:ApplicationBootstrap", settings.Keys, StringComparer.Ordinal);
        Assert.DoesNotContain("Unrelated:ServiceCredential", settings.Keys, StringComparer.Ordinal);
        Assert.DoesNotContain("synthetic-service-credential", string.Join('|', settings.Values), StringComparison.Ordinal);
        var oldKey = await File.ReadAllBytesAsync(Path.Combine(source, ".installation.key"), CancellationToken.None).ConfigureAwait(true);
        var newKey = await File.ReadAllBytesAsync(Path.Combine(destination, ".installation.key"), CancellationToken.None).ConfigureAwait(true);
        try { Assert.False(CryptographicOperations.FixedTimeEquals(oldKey, newKey)); }
        finally { CryptographicOperations.ZeroMemory(oldKey); CryptographicOperations.ZeroMemory(newKey); }
        Assert.DoesNotContain("synthetic-policy-value", Encoding.UTF8.GetString(await File.ReadAllBytesAsync(Path.Combine(destination, ".env"), CancellationToken.None).ConfigureAwait(true)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnapprovedEscrowAndExistingDestinationAreRefusedBeforeEffectsAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var destination = CreateEnvironment(directory.Path, "export");
        await Assert.ThrowsAsync<InvalidOperationException>(() => PortablePolicyEscrow.ExportAsync(destination, "synthetic-site", PortablePolicyArchive.Empty,
            allowCredentialEscrow: false, CancellationToken.None)).ConfigureAwait(true);
        Assert.Empty(Directory.EnumerateFileSystemEntries(destination));
        var marker = Path.Combine(destination, "existing");
        await File.WriteAllTextAsync(marker, "retained", CancellationToken.None).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(() => PortablePolicyEscrow.ExportAsync(destination, "synthetic-site", PortablePolicyArchive.Empty,
            allowCredentialEscrow: true, CancellationToken.None)).ConfigureAwait(true);
        Assert.Equal("retained", await File.ReadAllTextAsync(marker, CancellationToken.None).ConfigureAwait(true));
        Assert.False(File.Exists(Path.Combine(destination, ".env")));
    }

    [Fact]
    public async Task ForeignSiteAndMissingKeyCannotLoadProtectedEscrowAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var directory = new RegistryStateDirectory();
        var destination = CreateEnvironment(directory.Path, "export");
        await PortablePolicyEscrow.ExportAsync(destination, "synthetic-site", new PortablePolicyArchive("{}"), allowCredentialEscrow: true, CancellationToken.None).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(() => PortablePolicyEscrow.LoadAsync(destination, "foreign-site", allowCredentialEscrow: true, CancellationToken.None)).ConfigureAwait(true);
        File.Delete(Path.Combine(destination, ".installation.key"));
        await Assert.ThrowsAnyAsync<IOException>(() => PortablePolicyEscrow.LoadAsync(destination, "synthetic-site", allowCredentialEscrow: true, CancellationToken.None)).ConfigureAwait(true);
        Assert.False(File.Exists(Path.Combine(destination, ".installation.key")));
    }

    private static string CreateEnvironment(string parent, string name)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Synthetic escrow requires Linux.");
        var path = Path.Combine(parent, name);
        Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }
}

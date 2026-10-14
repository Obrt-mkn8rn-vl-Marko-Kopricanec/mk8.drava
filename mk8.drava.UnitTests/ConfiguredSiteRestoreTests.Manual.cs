using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Backup;
using Mk8.Drava.Application.DAL.Configuration.Loading;
using Mk8.Drava.Application.DAL.NoConf;
using Mk8.Drava.Application.DAL.Registry;
using Mk8.Drava.Application.Recovery;
using Mk8.Drava.Configuration;
using Supprocom.Secrets;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed partial class ConfiguredSiteRestoreTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("outside-inventory")]
    [InlineData("password-environment")]
    public async Task LegacyApprovedManualFilesImportWithoutReadingExternalCredentialLocatorsAsync(string variant)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var source = new RegistryStateDirectory();
        using var gateway = new RegistryStateDirectory();
        using var parent = new RegistryStateDirectory();
        using var certificates = DevelopmentUpstreamTrustCertificates.Create();
        var destination = Path.Combine(parent.Path, "site");
        var snapshot = Path.Combine(parent.Path, "snapshot");
        var inputs = await WriteLegacyManualInputsAsync(source.Path, destination, certificates.Leaf, variant).ConfigureAwait(true);
        var repository = await SqliteRegistryRepository.OpenAsync(source.Path, "site", CancellationToken.None).ConfigureAwait(true);
        await repository.DisposeAsync().ConfigureAwait(true);
        var backup = await SiteRecoveryBundle.CreateAsync(source.Path, gateway.Path, snapshot, "site", MaximumRegistryBytes, 65536,
            inputs, allowCredentialEscrow: true, CancellationToken.None).ConfigureAwait(true);
        var request = Request(snapshot, destination, backup.ManifestSha256) with
        {
            MaximumFileBytes = 65536,
            ApprovedFiles = [new("proxy.json", Credential: false), new("site.json", Credential: false), new("site.pfx", Credential: true)],
            Placements = [new("proxy.json", "application/config/proxy.json"), new("site.json", "application/config/sites/site.json"), new("site.pfx", "material/site.pfx")],
        };
        if (!string.Equals(variant, "valid", StringComparison.Ordinal))
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => ConfiguredSiteRestore.PrepareAsync(request, CancellationToken.None).AsTask()).ConfigureAwait(true);
            Assert.False(Directory.Exists(destination));
            return;
        }
        var prepared = await ConfiguredSiteRestore.PrepareAsync(request, CancellationToken.None).ConfigureAwait(true);
        Assert.NotNull(prepared.Receipt.ManualConfiguration);
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(Path.Combine(destination, "roles", "application", "Environment", ".env"), CancellationToken.None).ConfigureAwait(true);
        await using var roleLifetime = role.ConfigureAwait(true);
        var input = await new PortableManualConfigurationSource(role).ReadAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Equal("site.json", UpstreamTrustConfigurationTests.Only(input.Sites).Name);
        var bytes = input.ResolveCertificate("site").CopyPfx();
        try
        {
            using var restored = X509CertificateLoader.LoadPkcs12(bytes, "synthetic-legacy-password", X509KeyStorageFlags.UserKeySet);
            Assert.Equal(certificates.Leaf.Thumbprint, restored.Thumbprint);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
        foreach (var file in prepared.Receipt.Files)
        {
            var placement = UpstreamTrustConfigurationTests.Only(request.Placements.Where(item => string.Equals(item.Name, file.Name, StringComparison.Ordinal)).ToArray());
            Assert.Equal(file.Sha256, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(destination, placement.RelativePath), CancellationToken.None).ConfigureAwait(true))));
        }
    }

    private static async Task<RecoveryFileInput[]> WriteLegacyManualInputsAsync(string source, string destination, X509Certificate2 certificate, string variant)
    {
        var pfx = certificate.Export(X509ContentType.Pkcs12, "synthetic-legacy-password");
        try { await File.WriteAllBytesAsync(Path.Combine(source, "site.pfx"), pfx, CancellationToken.None).ConfigureAwait(false); }
        finally { CryptographicOperations.ZeroMemory(pfx); }
        var options = new ProxyOperationalOptions
        {
            Certificates = [new CertificateOptions { Id = "site", Path = Path.Combine(destination, "material", string.Equals(variant, "outside-inventory", StringComparison.Ordinal) ? "unapproved.pfx" : "site.pfx"),
                Password = string.Equals(variant, "password-environment", StringComparison.Ordinal) ? null : "synthetic-legacy-password",
                PasswordEnvironmentVariable = string.Equals(variant, "password-environment", StringComparison.Ordinal) ? "SYNTHETIC_UNREAD_PASSWORD" : null, }],
        };
        await File.WriteAllTextAsync(Path.Combine(source, "proxy.json"), JsonSerializer.Serialize(options, SiteConfigurationParser.ReadJsonOptions), CancellationToken.None).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(source, "site.json"), "{\"name\":\"legacy\",\"host\":\"legacy.test\",\"listeners\":[{\"name\":\"http\",\"address\":\"127.0.0.1\",\"port\":18080}],\"pathPrefix\":\"/\",\"upstreams\":[{\"name\":\"backend\",\"address\":\"127.0.0.1\",\"port\":9000}]}", CancellationToken.None).ConfigureAwait(false);
        var inputs = new[] { new RecoveryFileInput(Path.Combine(source, "proxy.json"), "proxy.json", Credential: false),
            new RecoveryFileInput(Path.Combine(source, "site.json"), "site.json", Credential: false), new RecoveryFileInput(Path.Combine(source, "site.pfx"), "site.pfx", Credential: true), };
        if (!OperatingSystem.IsWindows()) foreach (var input in inputs) File.SetUnixFileMode(input.SourcePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return inputs;
    }

    [Fact]
    public async Task RuntimeRecoveryPreservesManualInputsAndPfxUnderFreshRoleKeysAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var source = new RegistryStateDirectory();
        using var gateway = new RegistryStateDirectory();
        using var parent = new RegistryStateDirectory();
        using var certificates = DevelopmentUpstreamTrustCertificates.Create();
        var path = await InitializeManualRecoveryAsync(source.Path, certificates.Leaf).ConfigureAwait(true);
        var snapshot = Path.Combine(parent.Path, "snapshot");
        var backup = await SiteRecoveryBundle.CreateRuntimeAsync(source.Path, gateway.Path, snapshot, "site", MaximumRegistryBytes,
            maximumFileBytes: 1, files: [], path, allowCredentialEscrow: true, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(3, backup.Receipt.SchemaVersion);
        var escrow = Assert.IsType<PolicyEscrowReceipt>(backup.Receipt.Registry.PolicyEscrow);
        Assert.Equal(2, escrow.SchemaVersion);
        Assert.NotNull(escrow.ManualConfiguration);
        var destination = Path.Combine(parent.Path, "site");
        var request = Request(snapshot, destination, backup.ManifestSha256) with
        { SchemaVersion = 3, MaximumFileBytes = 1, ApprovedFiles = [], Placements = [], };
        var prepared = await ConfiguredSiteRestore.PrepareAsync(request, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(escrow.ManualConfiguration, prepared.Receipt.ManualConfiguration);
        Assert.False(Directory.Exists(Path.Combine(destination, "application", "policy")));
        var restoredPath = Path.Combine(destination, "roles", "application", "Environment", ".env");
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(restoredPath, CancellationToken.None).ConfigureAwait(true);
        await using var roleLifetime = role.ConfigureAwait(true);
        var input = await new PortableManualConfigurationSource(role).ReadAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Equal("manual.json", UpstreamTrustConfigurationTests.Only(input.Sites).Name);
        var bytes = input.ResolveCertificate("site").CopyPfx();
        try
        {
            using var restored = X509CertificateLoader.LoadPkcs12(bytes, "synthetic-recovery-password", X509KeyStorageFlags.UserKeySet);
            Assert.Equal(certificates.Leaf.Thumbprint, restored.Thumbprint);
            Assert.True(restored.HasPrivateKey);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
        Assert.False(Directory.Exists(Path.Combine(destination, "application", "config")));
        Assert.False(Directory.Exists(Path.Combine(destination, "application", "certs")));
        Assert.NotEqual(await File.ReadAllBytesAsync(Path.Combine(source.Path, ".installation.key"), CancellationToken.None).ConfigureAwait(true),
            await File.ReadAllBytesAsync(Path.Combine(Path.GetDirectoryName(restoredPath) ?? throw new InvalidDataException("Missing role parent."), ".installation.key"), CancellationToken.None).ConfigureAwait(true));
    }

    [Fact]
    public async Task PolicyOnlyBackupRefusesToOmitProtectedManualInputsAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var source = new RegistryStateDirectory();
        using var parent = new RegistryStateDirectory();
        using var certificates = DevelopmentUpstreamTrustCertificates.Create();
        var path = await InitializeManualRecoveryAsync(source.Path, certificates.Leaf).ConfigureAwait(true);
        await Assert.ThrowsAsync<InvalidDataException>(() => RegistrySnapshot.CreateProtectedAsync(source.Path, Path.Combine(parent.Path, "refused"),
            "site", MaximumRegistryBytes, path, allowCredentialEscrow: true, CancellationToken.None).AsTask()).ConfigureAwait(true);
        Assert.Empty(Directory.EnumerateFileSystemEntries(parent.Path));
    }

    [Theory]
    [InlineData("manual-receipt")]
    [InlineData("schema")]
    public async Task UnapprovedRuntimeImageCannotPublishAsync(string fault)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var source = new RegistryStateDirectory();
        using var parent = new RegistryStateDirectory();
        using var certificates = DevelopmentUpstreamTrustCertificates.Create();
        var path = await InitializeManualRecoveryAsync(source.Path, certificates.Leaf).ConfigureAwait(true);
        var snapshot = Path.Combine(parent.Path, "snapshot");
        var receipt = await RegistrySnapshot.CreateRuntimeAsync(source.Path, snapshot, "site", MaximumRegistryBytes, path,
            allowCredentialEscrow: true, CancellationToken.None).ConfigureAwait(true);
        var escrow = Assert.IsType<PolicyEscrowReceipt>(receipt.PolicyEscrow);
        var manual = Assert.IsType<ManualConfigurationReceipt>(escrow.ManualConfiguration);
        receipt = receipt with { PolicyEscrow = escrow with { ManualConfiguration = manual with { Utf8Bytes = manual.Utf8Bytes + 1 } } };
        var destination = Path.Combine(parent.Path, "refused");
        if (string.Equals(fault, "schema", StringComparison.Ordinal))
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => RegistrySnapshot.RestoreProtectedAsync(snapshot, destination, "site", MaximumRegistryBytes,
                        receipt, allowCredentialEscrow: true, CancellationToken.None).AsTask()).ConfigureAwait(true);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => RegistrySnapshot.RestoreRuntimeAsync(snapshot, destination, "site", MaximumRegistryBytes,
                        receipt, allowCredentialEscrow: true, CancellationToken.None).AsTask()).ConfigureAwait(true);
        }

        Assert.False(Directory.Exists(destination));
        Assert.Equal([snapshot], Directory.GetDirectories(parent.Path));
    }

    private static async Task<string> InitializeManualRecoveryAsync(string state, X509Certificate2 certificate)
    {
        var path = await ProtectedRegistrySnapshotTests.InitializeRoleAsync(state, state, "site").ConfigureAwait(false);
        var bytes = certificate.Export(X509ContentType.Pkcs12, "synthetic-recovery-password");
        PortableManualConfigurationDocument manual;
        try
        {
            manual = PortableManualConfigurationSource.Encode("site", "{\"certificates\":[{\"id\":\"site\",\"password\":\"synthetic-recovery-password\"}]}",
                [new ProxyManualSiteInput("manual.json", ProxyManualSiteFormat.Json,
                    "{\"name\":\"manual\",\"host\":\"manual.test\",\"listeners\":[{\"name\":\"http\",\"address\":\"127.0.0.1\",\"port\":18080}],\"pathPrefix\":\"/\",\"upstreams\":[{\"name\":\"backend\",\"address\":\"127.0.0.1\",\"port\":9000}]}")],
                [new ProxyManualCertificateInput("site", bytes)]);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(false);
        await using var lifetime = role.ConfigureAwait(false);
        var store = PortablePolicyPayloadStoreTests.Store(state);
        await store.UpdateDocumentAsync(settings => [.. settings, new SupprocomSecretSetting(PortableManualConfigurationSource.CollectionKey, manual.Reveal())], CancellationToken.None).ConfigureAwait(false);
        var repository = await SqliteRegistryRepository.OpenAsync(state, "site", new PortablePolicyPayloadStore(role), CancellationToken.None).ConfigureAwait(false);
        await repository.DisposeAsync().ConfigureAwait(false);
        return path;
    }
}

using Mk8.Drava.Application.BLL.ControlPlane.Backup;
using Mk8.Drava.Application.BLL.Registry;
using Mk8.Drava.Application.DAL.Registry;
using Mk8.Drava.Application.DAL.Storage;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class SiteRecoveryBundleTests
{
    private const long MaximumRegistryBytes = 16 * 1024 * 1024;
    // Opaque finite offline fixtures: no real credential, deployment configuration or serving authority.
    private static readonly RecoveryFileApproval[] Approvals = [new("application.json", Credential: false), new("administrator.token", Credential: true)];

    [Fact]
    public async Task BundleRetainsExplicitFilesAndRestoresClosedRegistryIntoNewPrivateDestinationAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var source = new RegistryStateDirectory();
        using var gateway = new RegistryStateDirectory();
        using var parent = new RegistryStateDirectory();
        await InitializeAsync(source.Path).ConfigureAwait(true);
        var inputs = await InputsAsync(source.Path).ConfigureAwait(true);
        var backup = Path.Combine(parent.Path, "backup");
        var created = await SiteRecoveryBundle.CreateAsync(source.Path, gateway.Path, backup, "site", MaximumRegistryBytes, 1024,
            inputs, allowCredentialEscrow: true, CancellationToken.None).ConfigureAwait(true);
        var restored = Path.Combine(parent.Path, "restored");
        var result = await SiteRecoveryBundle.RestoreAsync(backup, restored, "site", created.ManifestSha256, MaximumRegistryBytes, 1024,
            Approvals, allowCredentialEscrow: true, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(created.Receipt.Registry.RegistryRevision + 1, result.Receipt.Registry.RegistryRevision);
        Assert.NotEqual(created.ManifestSha256, result.ManifestSha256, StringComparer.Ordinal);
        foreach (var input in inputs)
        {
            Assert.Equal(await File.ReadAllBytesAsync(input.SourcePath, CancellationToken.None).ConfigureAwait(true),
                await File.ReadAllBytesAsync(Path.Combine(restored, "files", input.Name), CancellationToken.None).ConfigureAwait(true));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(restored, "files", input.Name)));
        }
        Assert.False(File.Exists(Path.Combine(restored, "gateway-serving.plan")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EitherActiveWriterRefusesEntireBundleAsync(bool application)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var source = new RegistryStateDirectory();
        using var gateway = new RegistryStateDirectory();
        using var parent = new RegistryStateDirectory();
        await InitializeAsync(source.Path).ConfigureAwait(true);
        var inputs = await InputsAsync(source.Path).ConfigureAwait(true);
        var held = PrivateOwnerFile.AcquireLease(Path.Combine(application ? source.Path : gateway.Path, application ? "application.lock" : "gateway.lock"));
        await using var heldLifetime = held.ConfigureAwait(true);
        await Assert.ThrowsAsync<IOException>(async () => await SiteRecoveryBundle.CreateAsync(source.Path, gateway.Path,
            Path.Combine(parent.Path, "refused"), "site", MaximumRegistryBytes, 1024, inputs, allowCredentialEscrow: true, CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.Empty(Directory.GetFileSystemEntries(parent.Path));
    }

    [Theory]
    [InlineData("credential")]
    [InlineData("bytes")]
    [InlineData("path")]
    [InlineData("link")]
    [InlineData("cancel")]
    [InlineData("classification")]
    [InlineData("canonical")]
    public async Task InvalidExportNeverPublishesAndReleasesBothWritersAsync(string defect)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var source = new RegistryStateDirectory();
        using var gateway = new RegistryStateDirectory();
        using var parent = new RegistryStateDirectory();
        await InitializeAsync(source.Path).ConfigureAwait(true);
        var inputs = await InputsAsync(source.Path).ConfigureAwait(true);
        if (string.Equals(defect, "classification", StringComparison.Ordinal)) inputs[1] = inputs[1] with { Credential = false };
        if (string.Equals(defect, "canonical", StringComparison.Ordinal))
        {
            var folder = Path.Combine(source.Path, "folder");
            Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            inputs[0] = inputs[0] with { SourcePath = Path.Combine(folder, "..", "input.json") };
        }
        if (string.Equals(defect, "path", StringComparison.Ordinal)) inputs[0] = inputs[0] with { Name = "../escape" };
        if (string.Equals(defect, "link", StringComparison.Ordinal))
        {
            var link = Path.Combine(source.Path, "link");
            File.CreateSymbolicLink(link, inputs[0].SourcePath);
            inputs[0] = inputs[0] with { SourcePath = link };
        }
        using var cancellation = new CancellationTokenSource();
        if (string.Equals(defect, "cancel", StringComparison.Ordinal)) await cancellation.CancelAsync().ConfigureAwait(true);
        async Task ActionAsync() => await SiteRecoveryBundle.CreateAsync(source.Path, gateway.Path, Path.Combine(parent.Path, "refused"),
            "site", MaximumRegistryBytes, string.Equals(defect, "bytes", StringComparison.Ordinal) ? 1 : 1024, inputs, !string.Equals(defect, "credential", StringComparison.Ordinal), cancellation.Token).ConfigureAwait(true);
        if (string.Equals(defect, "cancel", StringComparison.Ordinal)) await Assert.ThrowsAnyAsync<OperationCanceledException>(ActionAsync).ConfigureAwait(true);
        else await Assert.ThrowsAsync<InvalidDataException>(ActionAsync).ConfigureAwait(true);
        Assert.Empty(Directory.GetFileSystemEntries(parent.Path));
        var applicationLease = PrivateOwnerFile.AcquireLease(Path.Combine(source.Path, "application.lock"));
        await using var applicationLeaseLifetime = applicationLease.ConfigureAwait(true);
        var gatewayLease = PrivateOwnerFile.AcquireLease(Path.Combine(gateway.Path, "gateway.lock"));
        await using var gatewayLeaseLifetime = gatewayLease.ConfigureAwait(true);
    }

    [Theory]
    [InlineData("digest")]
    [InlineData("tamper")]
    [InlineData("extra")]
    [InlineData("registry-extra")]
    [InlineData("approval")]
    [InlineData("credential")]
    [InlineData("site")]
    public async Task InvalidRestoreNeverPublishesOrAltersOriginalBundleAsync(string defect)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var source = new RegistryStateDirectory();
        using var gateway = new RegistryStateDirectory();
        using var parent = new RegistryStateDirectory();
        await InitializeAsync(source.Path).ConfigureAwait(true);
        var backup = Path.Combine(parent.Path, "backup");
        var created = await SiteRecoveryBundle.CreateAsync(source.Path, gateway.Path, backup, "site", MaximumRegistryBytes, 1024,
            await InputsAsync(source.Path).ConfigureAwait(true), allowCredentialEscrow: true, CancellationToken.None).ConfigureAwait(true);
        var original = await File.ReadAllBytesAsync(Path.Combine(backup, "registry", "registry.sqlite"), CancellationToken.None).ConfigureAwait(true);
        if (string.Equals(defect, "tamper", StringComparison.Ordinal)) await File.WriteAllTextAsync(Path.Combine(backup, "files", "application.json"), "tampered", CancellationToken.None).ConfigureAwait(true);
        if (string.Equals(defect, "extra", StringComparison.Ordinal)) await File.WriteAllTextAsync(Path.Combine(backup, "files", "unknown"), "extra", CancellationToken.None).ConfigureAwait(true);
        if (string.Equals(defect, "registry-extra", StringComparison.Ordinal)) await File.WriteAllTextAsync(Path.Combine(backup, "registry", "unknown"), "extra", CancellationToken.None).ConfigureAwait(true);
        var destination = Path.Combine(parent.Path, "refused");
        await Assert.ThrowsAsync<InvalidDataException>(async () => await SiteRecoveryBundle.RestoreAsync(backup, destination,
            string.Equals(defect, "site", StringComparison.Ordinal) ? "other" : "site", string.Equals(defect, "digest", StringComparison.Ordinal) ? new string('A', 64) : created.ManifestSha256, MaximumRegistryBytes, 1024,
            string.Equals(defect, "approval", StringComparison.Ordinal) ? [new("different", Credential: false), Approvals[1]] : Approvals, !string.Equals(defect, "credential", StringComparison.Ordinal), CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
        Assert.False(Directory.Exists(destination));
        Assert.Equal([backup], Directory.GetDirectories(parent.Path));
        Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(backup, "registry", "registry.sqlite"), CancellationToken.None).ConfigureAwait(true));
    }

    [Fact]
    public async Task CallerInventoryMutationAfterEnumerationCannotChangeApprovedOutputPathsAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var source = new RegistryStateDirectory();
        using var gateway = new RegistryStateDirectory();
        using var parent = new RegistryStateDirectory();
        await InitializeAsync(source.Path).ConfigureAwait(true);
        var inputs = new MutatingInventory(await InputsAsync(source.Path).ConfigureAwait(true));
        var destination = Path.Combine(parent.Path, "backup");
        var result = await SiteRecoveryBundle.CreateAsync(source.Path, gateway.Path, destination, "site", MaximumRegistryBytes,
            1024, inputs, allowCredentialEscrow: true, CancellationToken.None).ConfigureAwait(true);
        inputs.Mutate();
        Assert.Equal("../escape", inputs[0].Name, StringComparer.Ordinal);
        Assert.Equal("application.json", result.Receipt.Files[0].Name, StringComparer.Ordinal);
        Assert.False(File.Exists(Path.Combine(destination, "escape")));
        Assert.Equal(await File.ReadAllBytesAsync(inputs[0].SourcePath, CancellationToken.None).ConfigureAwait(true),
            await File.ReadAllBytesAsync(Path.Combine(destination, "files", "application.json"), CancellationToken.None).ConfigureAwait(true));
    }

    // A caller may mutate its list after enumeration, including while the workflow awaits SQLite.
    private sealed class MutatingInventory(RecoveryFileInput[] entries) : IReadOnlyList<RecoveryFileInput>
    {
        private readonly RecoveryFileInput[] _entries = entries;
        private int _enumerations;
        public int Count => _entries.Length;
        public RecoveryFileInput this[int index] => _entries[index];
        public void Mutate() => _entries[0] = _entries[0] with { Name = "../escape" };
        IEnumerator<RecoveryFileInput> IEnumerable<RecoveryFileInput>.GetEnumerator() => Enumerate().GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => Enumerate().GetEnumerator();
        private IEnumerable<RecoveryFileInput> Enumerate()
        {
            var mutateAfter = ++_enumerations == 2;
            foreach (var entry in _entries) yield return entry;
            if (mutateAfter) Mutate();
        }
    }

    private static async Task InitializeAsync(string directory)
    {
        var repository = await SqliteRegistryRepository.OpenAsync(directory, "site", CancellationToken.None).ConfigureAwait(true);
        await using var lifetime = repository.ConfigureAwait(true);
        await repository.CommitAsync(0, new RegistryState(1, RegistryState.Empty.Grants, RegistryState.Empty.Instances, []),
            new RegistryAudit(DateTimeOffset.UnixEpoch, "fixture", "administrator", "site"), CancellationToken.None).ConfigureAwait(true);
    }

    private static async Task<RecoveryFileInput[]> InputsAsync(string directory)
    {
        var configuration = Path.Combine(directory, "input.json");
        var credential = Path.Combine(directory, "input.token");
        await File.WriteAllTextAsync(configuration, "opaque configuration fixture", CancellationToken.None).ConfigureAwait(true);
        await File.WriteAllTextAsync(credential, "finite synthetic token fixture", CancellationToken.None).ConfigureAwait(true);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(configuration, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.SetUnixFileMode(credential, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        return [new(configuration, "application.json", Credential: false), new(credential, "administrator.token", Credential: true)];
    }
}

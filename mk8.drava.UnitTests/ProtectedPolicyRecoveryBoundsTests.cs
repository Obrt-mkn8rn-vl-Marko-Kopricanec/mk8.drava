using System.Globalization;
using Microsoft.Data.Sqlite;
using Mk8.Drava.Application.BLL.ControlPlane.Backup;
using Mk8.Drava.Application.BLL.NoConf;
using Mk8.Drava.Application.DAL.NoConf;
using Mk8.Drava.Application.DAL.Registry;
using Mk8.Drava.Configuration;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class ProtectedPolicyRecoveryBoundsTests
{
    private const int OrdinaryLimit = 8 * 1024 * 1024;
    private const long RegistryLimit = 16 * 1024 * 1024;

    [Fact]
    public async Task TypedPolicyEscrowAboveEightMiBAndOrdinaryFileAtEightMiBRoundTripAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var source = new RegistryStateDirectory();
        using var gateway = new RegistryStateDirectory();
        using var parent = new RegistryStateDirectory();
        var (path, payloads) = await InitializeLargeHistoryAsync(source.Path).ConfigureAwait(true);
        var ordinary = await CreateSizedFileAsync(source.Path, OrdinaryLimit).ConfigureAwait(true);
        var backup = Path.Combine(parent.Path, "backup");
        var created = await SiteRecoveryBundle.CreateProtectedAsync(source.Path, gateway.Path, backup, "synthetic-site",
            RegistryLimit, OrdinaryLimit, [new RecoveryFileInput(ordinary, "ordinary.bin", Credential: false)], path,
            allowCredentialEscrow: true, CancellationToken.None).ConfigureAwait(true);
        var escrow = Assert.IsType<PolicyEscrowReceipt>(created.Receipt.Registry.PolicyEscrow);
        Assert.InRange(escrow.ArchiveBytes, OrdinaryLimit + 1L, InstallationCollectionBounds.MaximumDocumentBytes);
        Assert.Equal(32, escrow.References.Count);
        var restored = Path.Combine(parent.Path, "restored");
        var result = await SiteRecoveryBundle.RestoreVersionedAsync(backup, restored, "synthetic-site", created.ManifestSha256,
            RegistryLimit, OrdinaryLimit, [new RecoveryFileApproval("ordinary.bin", Credential: false)],
            allowCredentialEscrow: true, expectedSchemaVersion: 2, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(created.Receipt.Files, result.Receipt.Files);
        Assert.Equal(OrdinaryLimit, new FileInfo(Path.Combine(restored, "files", "ordinary.bin")).Length);
        var restoredEscrow = Assert.IsType<PolicyEscrowReceipt>(result.Receipt.Registry.PolicyEscrow);
        Assert.InRange(restoredEscrow.ArchiveBytes, OrdinaryLimit + 1L, InstallationCollectionBounds.MaximumDocumentBytes);
        Assert.Equal(escrow.References, restoredEscrow.References);
        await AssertRestoredHistoryAsync(Path.Combine(restored, "registry"), payloads, escrow.References).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrdinaryFileAboveEightMiBRefusesProtectedExportDespiteLargerTotalAllowanceAsync(bool credential)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var source = new RegistryStateDirectory();
        using var gateway = new RegistryStateDirectory();
        using var parent = new RegistryStateDirectory();
        var path = await ProtectedRegistrySnapshotTests.InitializeRoleAsync(source.Path, source.Path).ConfigureAwait(true);
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        await using (role.ConfigureAwait(true))
        {
            var repository = await SqliteRegistryRepository.OpenAsync(source.Path, "synthetic-site",
                new PortablePolicyPayloadStore(role), CancellationToken.None).ConfigureAwait(true);
            await repository.DisposeAsync().ConfigureAwait(true);
        }
        var ordinary = await CreateSizedFileAsync(source.Path, OrdinaryLimit + 1).ConfigureAwait(true);
        var before = await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true);
        var refused = Path.Combine(parent.Path, "refused");
        await Assert.ThrowsAsync<InvalidDataException>(() => SiteRecoveryBundle.CreateProtectedAsync(source.Path, gateway.Path,
            refused, "synthetic-site", RegistryLimit, 2L * OrdinaryLimit, [new RecoveryFileInput(ordinary, "ordinary.bin", credential)],
            path, allowCredentialEscrow: true, CancellationToken.None).AsTask()).ConfigureAwait(true);
        Assert.Empty(Directory.EnumerateFileSystemEntries(parent.Path));
        Assert.Equal(before, await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(OrdinaryLimit + 1, new FileInfo(ordinary).Length);
    }

    private static async Task<(string Path, PolicyPayloadSnapshot Payloads)> InitializeLargeHistoryAsync(string state)
    {
        var canonical = Enumerable.Range(1, 32).Select(static index =>
        {
            var prefix = "{\"id\":" + index.ToString(CultureInfo.InvariantCulture) + ",\"value\":\"";
            return prefix + new string('x', PolicyPayloadReference.MaximumUtf8Bytes - prefix.Length - 2) + "\"}";
        }).ToArray();
        var payloads = new PolicyPayloadSnapshot("synthetic-site", canonical.ToDictionary(
            static value => PolicyPayloadSnapshot.Describe(value).Digest, static value => value, StringComparer.Ordinal));
        var path = await ProtectedRegistrySnapshotTests.InitializeRoleAsync(state, state).ConfigureAwait(false);
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(false);
        await using (role.ConfigureAwait(false))
        {
            var store = new PortablePolicyPayloadStore(role);
            await store.RestoreAsync(PortablePolicyPayloadStoreTests.ArchiveFixture(payloads), payloads.References, CancellationToken.None).ConfigureAwait(false);
            var repository = await SqliteRegistryRepository.OpenAsync(state, "synthetic-site", store, CancellationToken.None).ConfigureAwait(false);
            await repository.DisposeAsync().ConfigureAwait(false);
        }
        // Seed synthetic reference metadata once; export and restore exercise the actual public paths.
        await SeedHistoryAsync(state, canonical).ConfigureAwait(false);
        return (path, payloads);
    }

    private static async Task SeedHistoryAsync(string state, string[] canonical)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(state, "registry.sqlite"), Mode = SqliteOpenMode.ReadWrite, Pooling = false,
        }.ToString());
        await using var connectionLifetime = connection.ConfigureAwait(false);
        await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(CancellationToken.None).ConfigureAwait(false);
        await using var transactionLifetime = transaction.ConfigureAwait(false);
        var command = connection.CreateCommand();
        await using var commandLifetime = command.ConfigureAwait(false);
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO policy_revisions VALUES($revision,$digest,$bytes,0,'operator','control')";
        for (var index = 0; index < canonical.Length; index++)
        {
            var reference = PolicyPayloadSnapshot.Describe(canonical[index]);
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$revision", index + 1);
            command.Parameters.AddWithValue("$digest", Convert.FromHexString(reference.Digest));
            command.Parameters.AddWithValue("$bytes", reference.Utf8Bytes);
            await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
        }
        command.Parameters.Clear();
        command.CommandText = "UPDATE policy_state SET revision=$revision WHERE id=1";
        command.Parameters.AddWithValue("$revision", canonical.Length);
        await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task AssertRestoredHistoryAsync(string state, PolicyPayloadSnapshot original, IReadOnlyList<PolicyPayloadReference> references)
    {
        var archive = await PortablePolicyEscrow.LoadAsync(Path.Combine(state, "policy"), "synthetic-site",
            allowCredentialEscrow: true, CancellationToken.None).ConfigureAwait(false);
        using var environment = new RegistryStateDirectory();
        var path = await ProtectedRegistrySnapshotTests.InitializeRoleAsync(environment.Path, state).ConfigureAwait(false);
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(false);
        await using var roleLifetime = role.ConfigureAwait(false);
        var store = new PortablePolicyPayloadStore(role);
        await store.RestoreAsync(archive, references, CancellationToken.None).ConfigureAwait(false);
        var repository = await SqliteRegistryRepository.OpenAsync(state, "synthetic-site", store, CancellationToken.None).ConfigureAwait(false);
        await using var repositoryLifetime = repository.ConfigureAwait(false);
        var history = await repository.ReadPolicyHistoryAsync(CancellationToken.None).ConfigureAwait(false);
        Assert.Equal(32, history.Count);
        for (var index = 0; index < history.Count; index++)
        {
            Assert.Equal(32 - index, history[index].Revision);
            Assert.Equal(original.Resolve(references[index]), history[index].CanonicalJson);
        }
    }

    private static async Task<string> CreateSizedFileAsync(string state, int bytes)
    {
        var path = Path.Combine(state, "ordinary.bin");
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Options = FileOptions.Asynchronous };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        var file = new FileStream(path, options);
        await using var lifetime = file.ConfigureAwait(false);
        file.SetLength(bytes);
        await file.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        return path;
    }
}

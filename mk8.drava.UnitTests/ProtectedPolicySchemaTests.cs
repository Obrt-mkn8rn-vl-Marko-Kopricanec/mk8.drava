using Microsoft.Data.Sqlite;
using Mk8.Drava.Application.BLL.NoConf;
using Mk8.Drava.Application.DAL.NoConf;
using Mk8.Drava.Application.DAL.Registry;
using Mk8.Drava.Configuration;
using Xunit;

namespace Mk8.Drava.UnitTests;

public sealed class ProtectedPolicySchemaTests
{
    private const long MaximumBytes = 16 * 1024 * 1024;

    [Theory]
    [InlineData("extra-column")]
    [InlineData("generated-column")]
    [InlineData("legacy-table")]
    [InlineData("policy-trigger")]
    [InlineData("view")]
    [InlineData("text-digest")]
    [InlineData("wrong-type")]
    [InlineData("schema")]
    [InlineData("registry-trigger")]
    public async Task AlteredProtectedSchemaRefusesStartupAndExportWithoutArchiveOrPublicationEffectsAsync(string alteration)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var source = new RegistryStateDirectory();
        using var destination = new RegistryStateDirectory();
        var path = await InitializeAcceptedPolicyAsync(source.Path).ConfigureAwait(true);
        await AlterDatabaseAsync(source.Path, alteration).ConfigureAwait(true);
        var databaseBefore = await File.ReadAllBytesAsync(Path.Combine(source.Path, "registry.sqlite"), CancellationToken.None).ConfigureAwait(true);
        var archiveBefore = await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true);
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        await using (role.ConfigureAwait(true))
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => SqliteRegistryRepository.OpenAsync(source.Path, "synthetic-site",
                new PortablePolicyPayloadStore(role), CancellationToken.None).AsTask()).ConfigureAwait(true);
        }
        var refused = Path.Combine(destination.Path, "refused");
        await Assert.ThrowsAsync<InvalidDataException>(() => RegistrySnapshot.CreateProtectedAsync(source.Path, refused,
            "synthetic-site", MaximumBytes, path, allowCredentialEscrow: true, CancellationToken.None).AsTask()).ConfigureAwait(true);
        Assert.False(Directory.Exists(refused));
        Assert.Empty(Directory.EnumerateFileSystemEntries(destination.Path));
        Assert.Equal(databaseBefore, await File.ReadAllBytesAsync(Path.Combine(source.Path, "registry.sqlite"), CancellationToken.None).ConfigureAwait(true));
        Assert.Equal(archiveBefore, await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(true));
    }

    [Fact]
    public async Task PendingPlaintextCleanupRefusesExportAndOwnedStartupCompletesBeforeAcceptanceAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var source = new RegistryStateDirectory();
        using var destination = new RegistryStateDirectory();
        var path = await InitializeAcceptedPolicyAsync(source.Path).ConfigureAwait(true);
        await AlterDatabaseAsync(source.Path, "pending-cleanup").ConfigureAwait(true);
        var snapshot = Path.Combine(destination.Path, "snapshot");
        await Assert.ThrowsAsync<InvalidDataException>(() => RegistrySnapshot.CreateProtectedAsync(source.Path, snapshot,
            "synthetic-site", MaximumBytes, path, allowCredentialEscrow: true, CancellationToken.None).AsTask()).ConfigureAwait(true);
        Assert.Empty(Directory.EnumerateFileSystemEntries(destination.Path));
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(true);
        await using (role.ConfigureAwait(true))
        {
            var repository = await SqliteRegistryRepository.OpenAsync(source.Path, "synthetic-site",
                new PortablePolicyPayloadStore(role), CancellationToken.None).ConfigureAwait(true);
            await using (repository.ConfigureAwait(true))
            {
                var accepted = Assert.IsType<PolicyRevision>(await repository.ReadPolicyAsync(CancellationToken.None).ConfigureAwait(true));
                Assert.Equal("{\"value\":1}", accepted.CanonicalJson);
                Assert.True(await repository.TryCommitPolicyAsync(1, 0, Revision(2), CancellationToken.None).ConfigureAwait(true));
            }
        }
        var receipt = await RegistrySnapshot.CreateProtectedAsync(source.Path, snapshot, "synthetic-site", MaximumBytes,
            path, allowCredentialEscrow: true, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(2, receipt.PolicyRevision);
        Assert.NotNull(receipt.PolicyEscrow);
    }

    private static async Task<string> InitializeAcceptedPolicyAsync(string state)
    {
        var path = await ProtectedRegistrySnapshotTests.InitializeRoleAsync(state, state).ConfigureAwait(false);
        var role = await PortableConfiguration.LoadRoleDocumentAsync<ApplicationBootstrap>(path, CancellationToken.None).ConfigureAwait(false);
        await using var roleLifetime = role.ConfigureAwait(false);
        var repository = await SqliteRegistryRepository.OpenAsync(state, "synthetic-site",
            new PortablePolicyPayloadStore(role), CancellationToken.None).ConfigureAwait(false);
        await using var repositoryLifetime = repository.ConfigureAwait(false);
        Assert.True(await repository.TryCommitPolicyAsync(0, 0, Revision(1), CancellationToken.None).ConfigureAwait(false));
        return path;
    }

    private static PolicyRevision Revision(int revision) => new(revision, revision == 1 ? "{\"value\":1}" : "{\"value\":2}",
        DateTimeOffset.UnixEpoch, "operator", "control");

    private static async Task AlterDatabaseAsync(string directory, string alteration)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, "registry.sqlite"), Mode = SqliteOpenMode.ReadWrite, Pooling = false,
        }.ToString());
        await using var connectionLifetime = connection.ConfigureAwait(false);
        await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        var command = connection.CreateCommand();
        await using var commandLifetime = command.ConfigureAwait(false);
        switch (alteration)
        {
            case "extra-column":
                command.CommandText = "ALTER TABLE policy_revisions ADD COLUMN canonical BLOB";
                await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
                return;
            case "generated-column":
                command.CommandText = "ALTER TABLE policy_revisions ADD COLUMN plaintext TEXT GENERATED ALWAYS AS ('synthetic-secret') VIRTUAL";
                await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
                return;
            case "legacy-table":
                command.CommandText = "CREATE TABLE policy_revisions_legacy(canonical BLOB)";
                await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
                return;
            case "policy-trigger":
                command.CommandText = "CREATE TRIGGER retain_plaintext AFTER INSERT ON policy_revisions BEGIN UPDATE policy_state SET cleanup_complete=0; END";
                await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
                return;
            case "view":
                command.CommandText = "CREATE VIEW policy_plaintext AS SELECT * FROM policy_revisions";
                await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
                return;
            case "text-digest":
                command.CommandText = "UPDATE policy_revisions SET payload_digest='01234567890123456789012345678901'";
                await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
                return;
            case "wrong-type":
                command.CommandText = "ALTER TABLE policy_revisions RENAME TO changed; CREATE TABLE policy_revisions(revision INTEGER PRIMARY KEY,payload_digest TEXT NOT NULL,payload_bytes INTEGER NOT NULL,at_utc INTEGER NOT NULL,actor TEXT NOT NULL,source TEXT NOT NULL); INSERT INTO policy_revisions SELECT * FROM changed; DROP TABLE changed";
                await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
                return;
            case "schema":
                command.CommandText = "UPDATE policy_state SET schema_version=3";
                await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
                return;
            case "registry-trigger":
                command.CommandText = "CREATE TRIGGER alter_policy AFTER UPDATE ON registry_state BEGIN UPDATE policy_state SET cleanup_complete=0; END";
                await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
                return;
            case "pending-cleanup":
                command.CommandText = "UPDATE policy_state SET cleanup_complete=0";
                await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
                return;
            default: throw new InvalidOperationException("Unknown synthetic SQL alteration.");
        }
    }
}
